using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Threading.Channels;
using WouterVanRanst.Utils.Extensions;
using Zio;

namespace Arius.Core.Features.Restore;

public sealed record ProgressUpdate;

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




            var rds = toRehydrateList.Select(pfe => new RehydrationDetail
            {
                RelativeName = pfe.RelativeName.RemoveSuffix(PointerFile.Extension),
                ArchivedSize = pfe.BinaryProperties.ArchivedSize.Value
            }).ToArray();
            if (rds.Any())
            {
                var rehydrateDecision = handlerContext.Request.RehydrationQuestionHandler(rds);
                if (rehydrateDecision)
                {
                    foreach (var g in toRehydrateList.GroupBy(pfe => pfe.BinaryProperties.ParentHash ?? pfe.BinaryProperties.Hash))
                    {
                        await handlerContext.ArchiveStorage.StartHydrationAsync(g.Key, RehydratePriority.Standard);

                        foreach (var pfe in g)
                        {
                            stillRehydratingList.Add(pfe);
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // TODO
            throw;
        }

        var r = new RestoreCommandResult
        {
            Rehydrating = stillRehydratingList.Select(pfe => new RehydrationDetail
            {
                RelativeName = pfe.RelativeName.RemoveSuffix(PointerFile.Extension),
                ArchivedSize = pfe.BinaryProperties.ArchivedSize.Value
            }).ToArray()
        };

        return r;
    }

    private Task CreateIndexTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Task.Run(async () =>
        {
            try
            {
                // 1. Iterate over all targets, and establish which binaryfiles need to be restored
                foreach (var targetPath in handlerContext.Targets)
                {
                    var binaryFileTargetPath = targetPath.IsPointerFilePath() ? targetPath.GetBinaryFilePath() : targetPath; // trim the pointerfile extension if present
                    var pfes = handlerContext.StateRepository.GetPointerFileEntries(binaryFileTargetPath.FullName, true).ToArray();

                    if (!pfes.Any())
                    {
                        logger.LogWarning($"Target {targetPath} was specified but no matching PointerFileEntry");
                    }

                    foreach (var pfe in pfes)
                    {
                        var fp = FilePair.FromPointerFileEntry(handlerContext.FileSystem, pfe);

                        if (handlerContext.Request.IncludePointers)
                        {
                            // Create PointerFiles
                            fp.PointerFile.Directory.Create();
                            fp.PointerFile.Write(pfe.Hash, pfe.CreationTimeUtc!.Value, pfe.LastWriteTimeUtc!.Value);
                        }

                        if (fp.BinaryFile.Exists)
                        {
                            // BinaryFile exists -- check the hash before restoring
                            await filePairsToHashChannel.Writer.WriteAsync(new (fp, pfe), cancellationToken);
                        }
                        else
                        {
                            // BinaryFile does not exist -- restore it
                            await filePairsToRestoreChannel.Writer.WriteAsync(new (fp, pfe), cancellationToken);

                        }
                    }
                }

                // only CreateIndexTask writes to the filePairsToHashChannel
                filePairsToHashChannel.Writer.Complete();
            }
            catch (OperationCanceledException)
            {
                filePairsToHashChannel.Writer.Complete(); // TODO can this be in the finally block?
                throw;
            }
            catch (Exception)
            {
                filePairsToHashChannel.Writer.Complete();
                errorCancellationTokenSource.Cancel();
                throw;
            }


        }, cancellationToken);

    private Task CreateHashTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Parallel.ForEachAsync(filePairsToHashChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions() { MaxDegreeOfParallelism = handlerContext.Request.HashParallelism, CancellationToken = cancellationToken },
            async (filePairWithPointerFileEntry, innerCancellationToken) =>
            {
                var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;

                var h = await handlerContext.Hasher.GetHashAsync(filePair);

                if (h == pointerFileEntry.Hash)
                {
                    // The hash matches - this binaryfile is already restored
                }
                else
                {
                    // The hash does not match - we need to restore this binaryfile
                    // TODO log a warning that the binary did not match, or should it stop execution?
                    await filePairsToRestoreChannel.Writer.WriteAsync(filePairWithPointerFileEntry, innerCancellationToken);
                }

            });

    private Task CreateDownloadBinariesTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Parallel.ForEachAsync(filePairsToRestoreChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions() { MaxDegreeOfParallelism = handlerContext.Request.DownloadParallelism, CancellationToken = cancellationToken },
            async (filePairWithPointerFileEntry, innerCancellationToken) =>
            {
                var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;

                try
                {
                    if (pointerFileEntry.BinaryProperties.ParentHash is not null)
                    {
                        await DownloadSmallFileAsync(handlerContext, filePairWithPointerFileEntry, innerCancellationToken);
                    }
                    else
                    {
                        await DownloadLargeFileAsync(handlerContext, filePairWithPointerFileEntry, innerCancellationToken);
                    }


                    //    // to rehydrate list

                    //    // todo hydrate
                }
                catch (InvalidDataException e) when (e.Message.Contains("The archive entry was compressed using an unsupported compression method."))
                {
                    logger.LogError($"Decryption failed for file '{filePair.BinaryFile.FullName}'. The passphrase may be incorrect or the file may be corrupted.");
                    errorCancellationTokenSource.Cancel();
                    throw;
                }
                catch (Exception e)
                {
                    throw;
                }
            });

    private async Task DownloadLargeFileAsync(HandlerContext handlerContext, FilePairWithPointerFileEntry filePairWithPointerFileEntry, CancellationToken cancellationToken = default)
    {
        var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;

        // 1. Get the decrypted blob stream from storage - use hydrated chunk for archived blobs
        await using var ss = await GetChunkStreamAsync(handlerContext, pointerFileEntry, cancellationToken);

        if (ss is null) // Chunk is not available (either archived or rehydrating)
            return;

        // 2. Write to the target file
        filePair.BinaryFile.Directory.Create();

        await using (var ts = filePair.BinaryFile.OpenWrite(pointerFileEntry.BinaryProperties.OriginalSize))
        {
            await ss.CopyToAsync(ts, cancellationToken);
            await ts.FlushAsync(cancellationToken); // Explicitly flush
        }

        filePair.BinaryFile.CreationTimeUtc  = pointerFileEntry.CreationTimeUtc!.Value;
        filePair.BinaryFile.LastWriteTimeUtc = pointerFileEntry.LastWriteTimeUtc!.Value;
    }

    private async Task DownloadSmallFileAsync(HandlerContext handlerContext, FilePairWithPointerFileEntry filePairWithPointerFileEntry, CancellationToken cancellationToken = default)
    {
        var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;

        // 1. Get the TarEntry
        var tar       = await GetCachedTarAsync();
        if (tar is null) // TAR is not available (either archived or rehydrating)
            return;

        await using var tarStream = tar.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var tarReader = new TarReader(tarStream);
        var             tarEntry  = await GetTarEntryAsync(pointerFileEntry.BinaryProperties.Hash);

        // TODO throw exception if tarEntry is null

        // 2. Write to the target file
        filePair.BinaryFile.Directory.Create();
        await using (var ts = filePair.BinaryFile.OpenWrite(pointerFileEntry.BinaryProperties.OriginalSize))
        {
            await tarEntry.DataStream.CopyToAsync(ts, cancellationToken);
            await ts.FlushAsync(cancellationToken); // Explicitly flush
        }

        filePair.BinaryFile.CreationTimeUtc  = pointerFileEntry.CreationTimeUtc!.Value;
        filePair.BinaryFile.LastWriteTimeUtc = pointerFileEntry.LastWriteTimeUtc!.Value;

        return;


        async Task<FileEntry?> GetCachedTarAsync()
        {
            var cachedBinary = handlerContext.BinaryCache.GetFileEntry(pointerFileEntry.BinaryProperties.ParentHash!.ToString());
            if (!cachedBinary.Exists)
            {
                // The TAR was not yet downloaded from blob storage
                await using var ss = await GetChunkStreamAsync(handlerContext, pointerFileEntry, cancellationToken);

                if (ss is null) // Chunk is not available (either archived or rehydrating)
                    return null;

                await using var ts = cachedBinary.Open(FileMode.CreateNew, FileAccess.Write, FileShare.None);

                await ss.CopyToAsync(ts, cancellationToken);
                await ts.FlushAsync(cancellationToken); // Explicitly flush
            }

            return cachedBinary;
        }

        async Task<TarEntry?> GetTarEntryAsync(Hash hash)
        {
            TarEntry? entry;
            while ((entry = await tarReader.GetNextEntryAsync(copyData: false /* todo investigate */, cancellationToken)) != null)
            {
                if (entry.Name == hash)
                    break;
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
}