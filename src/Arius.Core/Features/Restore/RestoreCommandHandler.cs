using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Humanizer;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Threading.Channels;
using WouterVanRanst.Utils.Extensions;
using Zio;

namespace Arius.Core.Features.Restore;

public abstract record ProgressUpdate;
public sealed record TaskProgressUpdate(string TaskName, double Percentage, string? StatusMessage = null) : ProgressUpdate;
public sealed record FileProgressUpdate(string FileName, double Percentage, string? StatusMessage = null) : ProgressUpdate; // TODO better/more consistent FileProgressUpdate

internal class RestoreCommandHandler : ICommandHandler<RestoreCommand, RestoreCommandResult>
{
    private readonly ILogger<RestoreCommandHandler> logger;
    private readonly ILoggerFactory                 loggerFactory;
    private readonly IOptions<AriusConfiguration>   config;

    public RestoreCommandHandler(
        ILogger<RestoreCommandHandler> logger,
        ILoggerFactory loggerFactory,
        IOptions<AriusConfiguration> config)
    {
        this.logger        = logger;
        this.loggerFactory = loggerFactory;
        this.config        = config;
    }

    private readonly Channel<FilePairWithPointerFileEntry> filePairsToRestoreChannel = ChannelExtensions.CreateBounded<FilePairWithPointerFileEntry>(capacity: 25, singleWriter: true, singleReader: false);
    private readonly Channel<FilePairWithPointerFileEntry> filePairsToHashChannel    = ChannelExtensions.CreateBounded<FilePairWithPointerFileEntry>(capacity: 25, singleWriter: true, singleReader: false);

    private record FilePairWithPointerFileEntry(FilePair FilePair, PointerFileEntry PointerFileEntry);

    public async ValueTask<RestoreCommandResult> Handle(RestoreCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await new HandlerContextBuilder(request, loggerFactory)
            .BuildAsync();

        return await Handle(handlerContext, cancellationToken);
    }

    internal async ValueTask<RestoreCommandResult> Handle(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting restore operation for {TargetCount} targets with hash parallelism {HashParallelism}, download parallelism {DownloadParallelism}", handlerContext.Targets.Length, handlerContext.Request.HashParallelism, handlerContext.Request.DownloadParallelism);
        
        using var errorCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var       errorCancellationToken       = errorCancellationTokenSource.Token;

        var indexTask            = CreateIndexTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        var hashTask             = CreateHashTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        var downloadBinariesTask = CreateDownloadBinariesTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        
        try
        {
            Task.WhenAll(indexTask, hashTask).ContinueWith(x =>
                {
                    // when both the indexTask and the hashTask are completed, nothing else will be written to the filePairsToRestoreChannel
                    filePairsToRestoreChannel.Writer.Complete();
                });
            await Task.WhenAll(indexTask, hashTask, downloadBinariesTask);

            // When all files have been downloaded, we know which files still need rehydration
            await CreateRehydrateTask(handlerContext, cancellationToken, errorCancellationTokenSource);

            // TODO reupload state file in case of changes (eg. tier in /chunks/ does not match)
        }
        catch (OperationCanceledException) when (!errorCancellationToken.IsCancellationRequested && cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Restore operation cancelled by user");
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Restore operation failed with exception");
            
            // Wait for all tasks to complete gracefully
            var allTasks = new[] { indexTask, hashTask, downloadBinariesTask };
            await Task.WhenAll(allTasks.Select(async t =>
            {
                try { await t; }
                catch { /* Ignore exceptions during graceful shutdown */ }
            }));
            
            throw;
        }

        var rehydratingFiles = stillRehydratingList.Select(pfe => new RehydrationDetail
        {
            RelativeName = pfe.RelativeName.RemoveSuffix(PointerFile.Extension),
            ArchivedSize = pfe.BinaryProperties.ArchivedSize
        }).ToArray();

        logger.LogInformation("Restore operation completed with {RehydratingCount} files still rehydrating", rehydratingFiles.Length);
        
        var r = new RestoreCommandResult
        {
            Rehydrating = rehydratingFiles
        };

        return r;
    }

    private Task CreateIndexTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Task.Run(async () =>
        {
            try
            {
                logger.LogInformation("Starting target indexing for {TargetCount} targets", handlerContext.Targets.Length);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Indexing targets...", 0));
                
                int totalFilesFound = 0;
                int filesToRestore = 0;
                int filesToVerify = 0;
                
                // 1. Iterate over all targets, and establish which binaryfiles need to be restored
                for (int i = 0; i < handlerContext.Targets.Length; i++)
                {
                    var targetPath = handlerContext.Targets[i];
                    var binaryFileTargetPath = targetPath.IsPointerFilePath() ? targetPath.GetBinaryFilePath() : targetPath; // trim the pointerfile extension if present
                    var pfes = handlerContext.StateRepository.GetPointerFileEntries(binaryFileTargetPath.FullName, true).ToArray();

                    if (!pfes.Any())
                    {
                        logger.LogWarning("Target {TargetPath} was specified but no matching PointerFileEntry found", targetPath);
                    }
                    else
                    {
                        logger.LogDebug("Found {FileCount} pointer file entries for target {TargetPath}", pfes.Length, targetPath);
                        totalFilesFound += pfes.Length;
                    }

                    foreach (var pfe in pfes)
                    {
                        var fp = FilePair.FromPointerFileEntry(handlerContext.FileSystem, pfe);

                        if (handlerContext.Request.IncludePointers)
                        {
                            // Create PointerFiles
                            logger.LogDebug("Creating pointer file for {FileName}", fp.PointerFile.FullName);
                            fp.PointerFile.Directory.Create();
                            fp.PointerFile.Write(pfe.Hash, pfe.CreationTimeUtc!.Value, pfe.LastWriteTimeUtc!.Value);
                        }

                        if (fp.BinaryFile.Exists)
                        {
                            // BinaryFile exists -- check the hash before restoring
                            logger.LogDebug("File {FileName} exists, queued for hash verification", fp.BinaryFile.FullName);
                            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(fp.BinaryFile.FullName, 10, "Already exists, to check..."));
                            await filePairsToHashChannel.Writer.WriteAsync(new (fp, pfe), cancellationToken);
                            filesToVerify++;
                        }
                        else
                        {
                            // BinaryFile does not exist -- restore it
                            logger.LogDebug("File {FileName} missing, queued for restore", fp.BinaryFile.FullName);
                            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(fp.BinaryFile.FullName, 50, "To restore..."));
                            await filePairsToRestoreChannel.Writer.WriteAsync(new (fp, pfe), cancellationToken);
                            filesToRestore++;
                        }
                    }
                    
                    // Update progress
                    var progressPercentage = (double)(i + 1) / handlerContext.Targets.Length * 100;
                    handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Indexing targets...", progressPercentage));
                }

                // only CreateIndexTask writes to the filePairsToHashChannel
                filePairsToHashChannel.Writer.Complete();
                
                logger.LogInformation("Target indexing completed: found {TotalFiles} files ({FilesToRestore} to restore, {FilesToVerify} to verify)", totalFilesFound, filesToRestore, filesToVerify);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Indexing targets...", 100, $"Found {totalFilesFound} files"));
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Target indexing cancelled");
                filePairsToHashChannel.Writer.Complete();
                throw;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Target indexing failed with exception");
                filePairsToHashChannel.Writer.Complete();
                errorCancellationTokenSource.Cancel();
                throw;
            }
        }, cancellationToken);

    private Task CreateHashTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource)
    {
        logger.LogInformation("Starting hash verification with parallelism {HashParallelism}", handlerContext.Request.HashParallelism);
        
        return Parallel.ForEachAsync(filePairsToHashChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions() { MaxDegreeOfParallelism = handlerContext.Request.HashParallelism, CancellationToken = cancellationToken },
            async (filePairWithPointerFileEntry, innerCancellationToken) =>
            {
                try
                {
                    var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;

                    var fileSizeFormatted = filePair.ExistingBinaryFile?.Length.Bytes().Humanize() ?? "0 B";
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 25, $"Verifying hash {fileSizeFormatted}..."));
                    
                    var h = await handlerContext.Hasher.GetHashAsync(filePair);

                    if (h == pointerFileEntry.Hash)
                    {
                        // The hash matches - this binaryfile is already restored
                        logger.LogDebug("File {FileName} hash verified, already restored", filePair.BinaryFile.FullName);
                        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 100, "Already restored"));
                    }
                    else
                    {
                        // The hash does not match - we need to restore this binaryfile
                        logger.LogWarning("File {FileName} hash mismatch (expected: {ExpectedHash}, actual: {ActualHash}), queued for restore", filePair.BinaryFile.FullName, pointerFileEntry.Hash.ToShortString(), h.ToShortString());
                        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 50, "Hash mismatch, restoring..."));
                        await filePairsToRestoreChannel.Writer.WriteAsync(filePairWithPointerFileEntry, innerCancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Hash verification failed for file {FileName}", filePairWithPointerFileEntry.FilePair.BinaryFile.FullName);
                    errorCancellationTokenSource.Cancel();
                    throw;
                }
            });
    }

    private Task CreateDownloadBinariesTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource)
    {
        logger.LogInformation("Starting binary download with parallelism {DownloadParallelism}", handlerContext.Request.DownloadParallelism);
        
        return Parallel.ForEachAsync(filePairsToRestoreChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions() { MaxDegreeOfParallelism = handlerContext.Request.DownloadParallelism, CancellationToken = cancellationToken },
            async (filePairWithPointerFileEntry, innerCancellationToken) =>
            {
                var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;

                try
                {
                    var fileSizeFormatted = pointerFileEntry.BinaryProperties.ArchivedSize.Bytes().Humanize();
                    if (pointerFileEntry.BinaryProperties.ParentHash is not null)
                    {
                        logger.LogDebug("File {FileName} is small file (parent hash: {ParentHash}), downloading from TAR", filePair.BinaryFile.FullName, pointerFileEntry.BinaryProperties.ParentHash.ToShortString());
                        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 60, $"Downloading {fileSizeFormatted} from TAR..."));
                        await DownloadSmallFileAsync(handlerContext, filePairWithPointerFileEntry, innerCancellationToken);
                    }
                    else
                    {
                        logger.LogDebug("File {FileName} is large file (hash: {Hash}), downloading directly", filePair.BinaryFile.FullName, pointerFileEntry.BinaryProperties.Hash.ToShortString());
                        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 60, $"Downloading {fileSizeFormatted}..."));
                        await DownloadLargeFileAsync(handlerContext, filePairWithPointerFileEntry, innerCancellationToken);
                    }
                }
                catch (InvalidDataException e) when (e.Message.Contains("The archive entry was compressed using an unsupported compression method."))
                {
                    logger.LogError("Decryption failed for file {FileName}. The passphrase may be incorrect or the file may be corrupted", filePair.BinaryFile.FullName);
                    errorCancellationTokenSource.Cancel();
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Download failed for file {FileName}", filePair.BinaryFile.FullName);
                    errorCancellationTokenSource.Cancel();
                    throw;
                }
            });
    }

    private async Task DownloadLargeFileAsync(HandlerContext handlerContext, FilePairWithPointerFileEntry filePairWithPointerFileEntry, CancellationToken cancellationToken = default)
    {
        var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;
        var fileSizeFormatted = pointerFileEntry.BinaryProperties.OriginalSize.Bytes().Humanize();

        logger.LogDebug("Starting large file download for {FileName} (size: {FileSize}, hash: {Hash})", filePair.BinaryFile.FullName, fileSizeFormatted, pointerFileEntry.Hash.ToShortString());
        //handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 30, "Getting chunk stream..."));

        // 1. Get the decrypted blob stream from storage - use hydrated chunk for archived blobs
        await using var ss = await GetChunkStreamAsync(handlerContext, pointerFileEntry, cancellationToken);

        if (ss is null) // Chunk is not available (either archived or rehydrating)
        {
            logger.LogDebug("Chunk stream not available for {FileName}, skipping download (archived or rehydrating)", filePair.BinaryFile.FullName);
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 100, "Skipped (rehydrating)"));
            return;
        }

        //handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 60, "Downloading..."));

        // 2. Write to the target file
        filePair.BinaryFile.Directory.Create();

        await using (var ts = filePair.BinaryFile.OpenWrite(pointerFileEntry.BinaryProperties.OriginalSize))
        {
            await ss.CopyToAsync(ts, cancellationToken);
            await ts.FlushAsync(cancellationToken); // Explicitly flush
        }

        filePair.BinaryFile.CreationTimeUtc  = pointerFileEntry.CreationTimeUtc!.Value;
        filePair.BinaryFile.LastWriteTimeUtc = pointerFileEntry.LastWriteTimeUtc!.Value;
        
        logger.LogInformation("Large file download completed: {FileName} ({FileSize})", filePair.BinaryFile.FullName, fileSizeFormatted);
        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 100, "Downloaded"));
    }

    private async Task DownloadSmallFileAsync(HandlerContext handlerContext, FilePairWithPointerFileEntry filePairWithPointerFileEntry, CancellationToken cancellationToken = default)
    {
        var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;
        var fileSizeFormatted = pointerFileEntry.BinaryProperties.OriginalSize.Bytes().Humanize();
        var parentHash = pointerFileEntry.BinaryProperties.ParentHash!;

        logger.LogDebug("Starting small file download for {FileName} from TAR (size: {FileSize}, parent hash: {ParentHash})", filePair.BinaryFile.FullName, fileSizeFormatted, parentHash.ToShortString());
        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 30, "Getting TAR archive..."));

        // 1. Get the TarEntry
        var tar = await GetCachedTarAsync();
        if (tar is null) // TAR is not available (either archived or rehydrating)
        {
            logger.LogDebug("TAR archive not available for {FileName}, skipping download (archived or rehydrating)", filePair.BinaryFile.FullName);
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 100, "Skipped (rehydrating)"));
            return;
        }

        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 50, "Extracting from TAR..."));
        
        await using var tarStream = tar.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var tarReader = new TarReader(tarStream);
        var tarEntry = await GetTarEntryAsync(pointerFileEntry.BinaryProperties.Hash);

        if (tarEntry is null)
        {
            logger.LogError("TAR entry not found for file {FileName} (hash: {Hash}) in TAR archive {ParentHash}", filePair.BinaryFile.FullName, pointerFileEntry.BinaryProperties.Hash.ToShortString(), parentHash.ToShortString());
            throw new InvalidOperationException($"TAR entry not found for file {filePair.BinaryFile.FullName}"); // TODO handle more graceful?
        }

        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 70, "Writing file..."));

        // 2. Write to the target file
        filePair.BinaryFile.Directory.Create();
        await using (var ts = filePair.BinaryFile.OpenWrite(pointerFileEntry.BinaryProperties.OriginalSize))
        {
            await tarEntry.DataStream!.CopyToAsync(ts, cancellationToken);
            await ts.FlushAsync(cancellationToken); // Explicitly flush
        }

        filePair.BinaryFile.CreationTimeUtc  = pointerFileEntry.CreationTimeUtc!.Value;
        filePair.BinaryFile.LastWriteTimeUtc = pointerFileEntry.LastWriteTimeUtc!.Value;

        logger.LogInformation("Small file download completed: {FileName} ({FileSize}) from TAR", filePair.BinaryFile.FullName, fileSizeFormatted);
        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.BinaryFile.FullName, 100, "Downloaded"));
        return;


        async Task<FileEntry?> GetCachedTarAsync()
        {
            var cachedBinary = handlerContext.BinaryCache.GetFileEntry(parentHash.ToString());
            if (!cachedBinary.Exists)
            {
                logger.LogDebug("TAR archive not cached, downloading from blob storage (parent hash: {ParentHash})", parentHash.ToShortString());
                
                // The TAR was not yet downloaded from blob storage
                await using var ss = await GetChunkStreamAsync(handlerContext, pointerFileEntry, cancellationToken);

                if (ss is null) // Chunk is not available (either archived or rehydrating)
                {
                    logger.LogDebug("TAR archive chunk stream not available for {ParentHash}", parentHash.ToShortString());
                    return null;
                }

                await using var ts = cachedBinary.Open(FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await ss.CopyToAsync(ts, cancellationToken);
                await ts.FlushAsync(cancellationToken); // Explicitly flush
                
                logger.LogDebug("TAR archive cached successfully for {ParentHash}", parentHash.ToShortString());
            }
            else
            {
                logger.LogDebug("Using cached TAR archive for {ParentHash}", parentHash.ToShortString());
            }

            return cachedBinary;
        }

        async Task<TarEntry?> GetTarEntryAsync(Hash hash)
        {
            logger.LogDebug("Searching for TAR entry with hash {Hash}", hash.ToShortString());
            
            TarEntry? entry;
            while ((entry = await tarReader.GetNextEntryAsync(copyData: false, cancellationToken)) != null)
            {
                if (entry.Name == hash)
                {
                    logger.LogDebug("Found TAR entry for hash {Hash}", hash.ToShortString());
                    break;
                }
            }

            if (entry is null)
            {
                logger.LogError("TAR entry not found for hash {Hash}", hash.ToShortString());
            }

            return entry;
        }
    }

    private readonly ConcurrentBag<PointerFileEntry> toRehydrateList = new();
    private readonly ConcurrentBag<PointerFileEntry> stillRehydratingList = new();

    private async Task<Stream?> GetChunkStreamAsync(HandlerContext handlerContext, PointerFileEntry pointerFileEntry, CancellationToken cancellationToken = default)
    {
        var hash = pointerFileEntry.BinaryProperties.ParentHash ?? pointerFileEntry.BinaryProperties.Hash;

        if (pointerFileEntry.BinaryProperties.StorageTier != StorageTier.Archive)
        {
            // This is supposed to be a blob in an online tier
            var result = await handlerContext.ArchiveStorage.OpenReadChunkAsync(hash, cancellationToken);
            switch (result)
            {
                case { IsSuccess: true }:
                    logger.LogInformation("Reading from hydrated blob {BlobName} for '{RelativeName}'.", hash, pointerFileEntry.RelativeName);
                    return result.Value;
                case { Errors: [BlobArchivedError { BlobName: var name }, ..] }:
                    // Blob is unexpectedly archived. Update the StateRepository with the correct state
                    logger.LogWarning("Blob {BlobName} for '{RelativeName}' is unexpectedly in the Archive tier. Updating StateDatabase & added to the rehydration list.", name, pointerFileEntry.RelativeName);
                    toRehydrateList.Add(pointerFileEntry);
                    handlerContext.StateRepository.SetBinaryPropertyArchiveTier(hash, StorageTier.Archive);
                    return null;
                case { Errors: [BlobRehydratingError { BlobName: var name }, ..] }:
                    // Blob is unexpectedly rehydrating in-place. Try again later
                    logger.LogInformation("Blob {BlobName} for '{RelativeName}' is still rehydrating. Try again later.", name, pointerFileEntry.RelativeName);
                    stillRehydratingList.Add(pointerFileEntry);
                    return null;
                case { Errors: [BlobNotFoundError { BlobName: var name }, ..] }:
                    // Blob not found
                    logger.LogError("Did not find blob {BlobName} for '{RelativeName}'. This binary is lost.", name, pointerFileEntry.RelativeName);
                    // TODO surface this better to the user
                    return null;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        else
        {
            // This blob needs to be/has been/is being rehydrated
            var result = await handlerContext.ArchiveStorage.OpenReadHydratedChunkAsync(hash, cancellationToken);
            switch (result)
            {
                case { IsSuccess: true }:
                    logger.LogInformation("Reading from rehydrated blob {BlobName} for '{RelativeName}'.", hash, pointerFileEntry.RelativeName);
                    return result.Value;
                case { Errors: [BlobNotFoundError { BlobName: var name }, ..] }:
                    // Blob not found in chunks-rehydrated --> add it to the rehydration list
                    logger.LogInformation("Blob {BlobName} for '{RelativeName}' is in the Archive tier. Added to the rehydration list.", name, pointerFileEntry.RelativeName);
                    toRehydrateList.Add(pointerFileEntry);
                    return null;
                case { Errors: [BlobRehydratingError { BlobName: var name }, ..] }:
                    // Blob is still rehydrating. Try again later
                    logger.LogInformation("Blob {BlobName} for '{RelativeName}' is still rehydrating. Try again later.", name, pointerFileEntry.RelativeName);
                    stillRehydratingList.Add(pointerFileEntry);
                    return null;
                case { Errors: [BlobArchivedError { BlobName: var name }, ..] }:
                    // Blob in chunks-rehydrated is unexpectedly in Archive tier - handle gracefully by starting rehydration again
                    logger.LogWarning("Blob {BlobName} for '{RelativeName}' is unexpectedly in the Archive tier. Hydrating it.", name, pointerFileEntry.RelativeName);
                    await handlerContext.ArchiveStorage.StartHydrationAsync(hash, RehydratePriority.Standard);
                    stillRehydratingList.Add(pointerFileEntry);
                    return null;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private Task CreateRehydrateTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Task.Run(async () =>
        {
            try
            {
                var rds = toRehydrateList.Select(pfe => new RehydrationDetail
                {
                    RelativeName = pfe.RelativeName.RemoveSuffix(PointerFile.Extension),
                    ArchivedSize = pfe.BinaryProperties.ArchivedSize
                }).ToArray();

                if (rds.Any())
                {
                    logger.LogInformation("Found {RehydrationCount} files requiring rehydration (total size: {TotalSize})", rds.Length, rds.Sum(rd => rd.ArchivedSize).Bytes().Humanize());

                    var rehydrateDecision = handlerContext.Request.RehydrationQuestionHandler(rds);
                    if (rehydrateDecision != RehydrationDecision.DoNotRehydrate)
                    {
                        logger.LogInformation("Starting rehydration with priority {Priority} for {BlobCount} unique blobs", rehydrateDecision.ToRehydratePriority(), toRehydrateList.GroupBy(pfe => pfe.BinaryProperties.ParentHash ?? pfe.BinaryProperties.Hash).Count());

                        handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Rehydration", 0));

                        foreach (var g in toRehydrateList.GroupBy(pfe => pfe.BinaryProperties.ParentHash ?? pfe.BinaryProperties.Hash))
                        {
                            await handlerContext.ArchiveStorage.StartHydrationAsync(g.Key, rehydrateDecision.ToRehydratePriority());
                            logger.LogDebug("Started rehydration for blob {BlobHash} covering {FileCount} files", g.Key.ToShortString(), g.Count());

                            foreach (var pfe in g)
                            {
                                stillRehydratingList.Add(pfe);
                            }
                        }

                        handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Rehydration", 100));
                    }
                    else
                    {
                        logger.LogInformation("User chose not to rehydrate {FileCount} archived files", rds.Length);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Rehydration cancelled");
                filePairsToHashChannel.Writer.Complete();
                throw;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Rehydration failed with exception");
                filePairsToHashChannel.Writer.Complete();
                errorCancellationTokenSource.Cancel();
                throw;
            }
        }, cancellationToken);

}