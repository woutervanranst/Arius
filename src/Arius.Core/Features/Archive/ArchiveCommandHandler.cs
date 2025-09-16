using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Humanizer;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Threading.Channels;
using Zio;

namespace Arius.Core.Features.Archive;

public abstract record ProgressUpdate;
public sealed record TaskProgressUpdate(string TaskName, double Percentage, string? StatusMessage = null) : ProgressUpdate;
public sealed record FileProgressUpdate(string FileName, double Percentage, string? StatusMessage = null) : ProgressUpdate;

internal class ArchiveCommandHandler : ICommandHandler<ArchiveCommand, Unit>
{
    private readonly ILogger<ArchiveCommandHandler> logger;
    private readonly ILoggerFactory                 loggerFactory;
    private readonly IOptions<AriusConfiguration>   config;

    public ArchiveCommandHandler(
        ILogger<ArchiveCommandHandler> logger,
        ILoggerFactory loggerFactory,
        IOptions<AriusConfiguration> config)
    {
        this.logger        = logger;
        this.loggerFactory = loggerFactory;
        this.config        = config;
    }

    private readonly Dictionary<Hash, TaskCompletionSource> uploadingHashes = new();

    private readonly Channel<FilePair>         indexedFilesChannel     = ChannelExtensions.CreateBounded<FilePair>(capacity: 20, singleWriter: true, singleReader: false);
    private readonly Channel<FilePairWithHash> hashedLargeFilesChannel = ChannelExtensions.CreateBounded<FilePairWithHash>(capacity: 10, singleWriter: false, singleReader: false);
    private readonly Channel<FilePairWithHash> hashedSmallFilesChannel = ChannelExtensions.CreateBounded<FilePairWithHash>(capacity: 10, singleWriter: false, singleReader: true);

    private record FilePairWithHash(FilePair FilePair, Hash Hash);

    public async ValueTask<Unit> Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await new HandlerContextBuilder(request, loggerFactory)
            .BuildAsync();

        return await Handle(handlerContext, cancellationToken);
    }

    internal async ValueTask<Unit> Handle(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting archive operation for path {LocalRoot} with parallelism {Parallelism}", handlerContext.Request.LocalRoot, handlerContext.Request.Parallelism);
        
        using var errorCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var       errorCancellationToken       = errorCancellationTokenSource.Token;

        var indexTask            = CreateIndexTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        var hashTask             = CreateHashTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        var uploadLargeFilesTask = CreateUploadLargeFilesTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        var uploadSmallFilesTask = CreateUploadSmallFilesTarArchiveTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);

        try
        {
            await Task.WhenAll(indexTask, hashTask, uploadLargeFilesTask, uploadSmallFilesTask);

            // 6. Remove PointerFileEntries that do not exist on disk
            logger.LogDebug("Cleaning up pointer file entries that no longer exist on disk");
            handlerContext.StateRepository.DeletePointerFileEntries(pfe => !handlerContext.FileSystem.FileExists(pfe.RelativeName));

            // 7. Upload the new state file to blob storage
            if (handlerContext.StateRepository.HasChanges)
            {
                var stateFileName = Path.GetFileNameWithoutExtension(handlerContext.StateRepository.StateDatabaseFile.Name);
                logger.LogInformation("Changes detected in database, uploading state file {StateFileName}", stateFileName);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Uploading state file...", 0));
                
                handlerContext.StateRepository.Vacuum();
                await handlerContext.ArchiveStorage.UploadStateAsync(stateFileName, handlerContext.StateRepository.StateDatabaseFile, cancellationToken);
                
                logger.LogInformation("Successfully uploaded state file {StateFileName}", stateFileName);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Uploading state file...", 100, "Completed"));
            }
            else
            {
                logger.LogInformation("No changes to the database. Skipping upload and deleting local state file.");
                handlerContext.StateRepository.Delete();
            }
        }
        catch (OperationCanceledException) when (!errorCancellationToken.IsCancellationRequested && cancellationToken.IsCancellationRequested)
        {
            // User-triggered cancellation - just re-throw
            throw;
        }
        catch (Exception)
        {
            // Either a task failed with an exception or error-triggered cancellation occurred
            // Wait for all tasks to complete gracefully
            var allTasks = new[] { indexTask, hashTask, uploadLargeFilesTask, uploadSmallFilesTask };
            await Task.WhenAll(allTasks.Select(async t =>
            {
                try { await t; }
                catch { /* Ignore exceptions during graceful shutdown */ }
            }));

            // Map tasks to their names for logging
            var taskNames = new Dictionary<Task, string>
            {
                { indexTask, nameof(indexTask) },
                { hashTask, nameof(hashTask) },
                { uploadLargeFilesTask, nameof(uploadLargeFilesTask) },
                { uploadSmallFilesTask, nameof(uploadSmallFilesTask) }
            };

            // Log cancelled tasks (debug level)
            var cancelledTasks = allTasks.Where(t => t.IsCanceled).ToArray();
            if (cancelledTasks.Any())
            {
                var cancelledTaskNames = cancelledTasks.Select(t => taskNames[t]).ToArray();
                logger.LogDebug("Tasks cancelled during graceful shutdown: {TaskNames}", string.Join(", ", cancelledTaskNames));
            }

            // Log and handle failed tasks (error level)
            var faultedTasks = allTasks.Where(t => t.IsFaulted).ToArray();
            if (faultedTasks.Any())
            {
                if (faultedTasks is { Length: 1 } && faultedTasks.Single() is var faultedTask)
                {
                    // Single faulted task - log the exception
                    var baseException = faultedTask.Exception!.GetBaseException();
                    logger.LogError(baseException, "Task '{TaskName}' failed with exception '{Exception}'", taskNames[faultedTask], baseException.Message);
                }
                else
                {
                    // Multiple faulted tasks - log the exceptions
                    var exceptions = faultedTasks.Select(t => t.Exception!.GetBaseException()).ToArray();
                    logger.LogError(new AggregateException("Multiple tasks failed during archive operation", exceptions), "Tasks failed: {TaskNames}", string.Join(", ", faultedTasks.Select(t => taskNames[t])));
                }
            }

            throw;
        }

        logger.LogInformation("Archive operation completed successfully for path {LocalRoot}", handlerContext.Request.LocalRoot);
        return Unit.Value;
    }

    private Task CreateIndexTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Task.Run(async () =>
        {
            try
            {
                logger.LogInformation("Starting file indexing in path {LocalRoot}", handlerContext.Request.LocalRoot);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Indexing files...", 0));

                int fileCount = 0;
                foreach (var fp in handlerContext.FileSystem.EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref fileCount);
                    await indexedFilesChannel.Writer.WriteAsync(FilePair.FromBinaryFileFileEntry(fp), cancellationToken);
                }

                indexedFilesChannel.Writer.Complete();

                logger.LogInformation("File indexing completed: found {FileCount} files in {LocalRoot}", fileCount, handlerContext.Request.LocalRoot);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Indexing files...", 100, $"Found {fileCount} files"));
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("File indexing cancelled");
                indexedFilesChannel.Writer.Complete();
                throw;
            }
            catch (Exception e)
            {
                logger.LogError(e, "File indexing failed with exception");
                indexedFilesChannel.Writer.Complete();
                errorCancellationTokenSource.Cancel();
                throw;
            }
        }, cancellationToken);

    private Task CreateHashTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource)
    {
        logger.LogInformation("Starting file hashing with parallelism {Parallelism}", handlerContext.Request.Parallelism);
        
        var t = Parallel.ForEachAsync(indexedFilesChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions { MaxDegreeOfParallelism = handlerContext.Request.Parallelism, CancellationToken = cancellationToken },
            async (filePair, innerCancellationToken) =>
            {
                try
                {
                    var fileSizeFormatted = filePair.ExistingBinaryFile?.Length.Bytes().Humanize() ?? "0 B";
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 10, $"Hashing {fileSizeFormatted}..."));

                    // 1. Hash the file
                    var h = await handlerContext.Hasher.GetHashAsync(filePair);

                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 50, "Waiting for upload..."));

                    var isSmallFile = filePair.BinaryFile.Length <= handlerContext.Request.SmallFileBoundary;
                    logger.LogDebug("File {FileName} hashed to {Hash}, routing to {FileType} processing (size: {FileSize})", filePair.FullName, h.ToShortString(), isSmallFile ? "small" : "large", fileSizeFormatted);

                    if (isSmallFile)
                        await hashedSmallFilesChannel.Writer.WriteAsync(new(filePair, h), cancellationToken: innerCancellationToken);
                    else
                        await hashedLargeFilesChannel.Writer.WriteAsync(new(filePair, h), cancellationToken: innerCancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "File hashing failed for {FileName}", filePair.FullName);
                    errorCancellationTokenSource.Cancel();
                    throw;
                }
            });

        t.ContinueWith(_ =>
        {
            logger.LogDebug("File hashing completed, closing channels");
            hashedSmallFilesChannel.Writer.Complete();
            hashedLargeFilesChannel.Writer.Complete();
        }, TaskContinuationOptions.ExecuteSynchronously);

        return t;
    }

    private Task CreateUploadLargeFilesTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Parallel.ForEachAsync(hashedLargeFilesChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions { MaxDegreeOfParallelism = handlerContext.Request.Parallelism, CancellationToken = cancellationToken },
            async (filePairWithHash, innerCancellationToken) =>
            {
                try
                {
                    await UploadLargeFileAsync(handlerContext, filePairWithHash, cancellationToken: innerCancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Large file upload task failed");
                    errorCancellationTokenSource.Cancel();
                    throw;
                }
            });

    private Task CreateUploadSmallFilesTarArchiveTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Task.Run(async () =>
        {
            try
            {
                await UploadSmallFileAsync(handlerContext, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Small files TAR archive task failed");
                errorCancellationTokenSource.Cancel();
                throw;
            }
        }, cancellationToken);

    private const string ChunkContentType = "application/aes256cbc+gzip";
    private const string TarChunkContentType = "application/aes256cbc+tar+gzip";

    internal async Task<(long OriginalSize, long ArchivedSize)> UploadIfNotExistsAsync(HandlerContext handlerContext, Hash hash, Stream sourceStream, CompressionLevel compressionLevel, string contentType, CancellationToken cancellationToken)
    {
        logger.LogDebug("Attempting to upload chunk with hash {Hash} using content type {ContentType}", hash.ToShortString(), contentType);

        // Try to open the blob for writing WITHOUT overwriting it
        var targetStreamResult = await handlerContext.ArchiveStorage.OpenWriteChunkAsync(hash, compressionLevel, contentType, progress: null, overwrite: false, cancellationToken: cancellationToken);

        if (targetStreamResult.IsSuccess)
        {
            logger.LogDebug("Chunk does not exist, performing new upload for hash {Hash}", hash.ToShortString());

            // New upload - perform the upload
            long originalSize, archivedSize;
            await using (var targetStream = targetStreamResult.Value)
            {
                await sourceStream.CopyToAsync(targetStream, bufferSize: 81920 /* todo optimize */, cancellationToken);
                await targetStream.FlushAsync(cancellationToken);

                originalSize = sourceStream.Position;
                archivedSize = targetStream.Position;
            }

            // Write Metadata
            var metadata = new Dictionary<string, string>
            {
                ["OriginalSize"] = originalSize.ToString(),
                ["ArchivedSize"] = archivedSize.ToString()
            };
            await handlerContext.ArchiveStorage.SetChunkMetadataAsync(hash, metadata);

            // Ensure correct storage tier
            await handlerContext.ArchiveStorage.SetChunkStorageTierPerPolicy(hash, archivedSize, handlerContext.Request.Tier);

            logger.LogDebug("Upload completed for hash {Hash}: original={OriginalSize}, archived={ArchivedSize}", hash.ToShortString(), originalSize, archivedSize);

            return (originalSize, archivedSize);
        }
        else if (targetStreamResult.HasError<BlobAlreadyExistsError>())
        {
            logger.LogInformation("Chunk already exists for hash {Hash}, checking content type", hash.ToShortString());

            // Blob exists - check content type
            var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, cancellationToken);

            if (properties?.ContentType == contentType)
            {
                logger.LogDebug("Chunk has correct content type for hash {Hash}", hash.ToShortString());

                // Correct content type: file was already uploaded previous time --> read from metadata

                // Correct content type - get size from metadata
                if (properties.Metadata != null &&
                    properties.Metadata.TryGetValue("OriginalSize", out var originalSizeStr) &&
                    properties.Metadata.TryGetValue("ArchivedSize", out var archivedSizeStr) &&
                    long.TryParse(originalSizeStr, out var originalSize) &&
                    long.TryParse(archivedSizeStr, out var archivedSize))
                {
                    logger.LogDebug("Using existing metadata for hash {Hash}: original={OriginalSize}, archived={ArchivedSize}", hash.ToShortString(), originalSize, archivedSize);

                    // Ensure correct storage tier
                    await handlerContext.ArchiveStorage.SetChunkStorageTierPerPolicy(hash, archivedSize, handlerContext.Request.Tier);

                    return (originalSize, archivedSize);
                }
            }

            // Incorrect content type or metadata not set: file was not properly uploaded last time --> delete and re-upload
            logger.LogWarning("Chunk exists with incorrect content type '{ActualContentType}' (expected '{ExpectedContentType}') for hash {Hash}, deleting and re-uploading", properties?.ContentType, contentType, hash.ToShortString());

            await handlerContext.ArchiveStorage.DeleteChunkAsync(hash, cancellationToken);

            // Recursive call to upload
            return await UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, contentType, cancellationToken);
        }
        else
        {
            var error = targetStreamResult.Errors.First();
            logger.LogError("Unexpected error during upload attempt for hash {Hash}: {Error}", hash.ToShortString(), error);
            throw new InvalidOperationException($"Unexpected error during upload: {error}");
        }
    }

    private async Task UploadLargeFileAsync(HandlerContext handlerContext, FilePairWithHash filePairWithHash, CancellationToken cancellationToken = default)
    {
        var (filePair, hash) = filePairWithHash;

        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
        var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, hash);

        // 3. Upload the Binary, if needed
        if (needsToBeUploaded)
        {
            var fileSizeFormatted = filePair.ExistingBinaryFile?.Length.Bytes().Humanize() ?? "0 B";
            logger.LogInformation("Uploading large file {FileName} ({FileSize}, hash: {Hash})", filePair.FullName, fileSizeFormatted, hash.ToShortString());
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, $"Uploading {fileSizeFormatted}..."));

            // Upload
            await using var sourceStream = filePair.BinaryFile.OpenRead();
            var (sourceStreamPosition, targetStreamPosition) = await UploadIfNotExistsAsync(
                handlerContext, hash, sourceStream, CompressionLevel.SmallestSize, ChunkContentType, cancellationToken);

            // Get the current tier (tier was already set in UploadIfNotExistsAsync)
            var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, cancellationToken);
            var actualTier = properties?.StorageTier ?? handlerContext.Request.Tier;

            // Add to db
            handlerContext.StateRepository.AddBinaryProperties(new BinaryProperties
            {
                Hash         = hash,
                OriginalSize = sourceStreamPosition,
                ArchivedSize = targetStreamPosition,
                StorageTier  = actualTier
            });

            var compressionRatio = sourceStreamPosition > 0 ? (double)targetStreamPosition / sourceStreamPosition : 1.0;
            logger.LogInformation("Large file upload completed: {FileName} (original: {OriginalSize}, archived: {ArchivedSize}, compression: {CompressionRatio:P1}, tier: {StorageTier})", filePair.FullName, sourceStreamPosition.Bytes().Humanize(), targetStreamPosition.Bytes().Humanize(), compressionRatio, actualTier);

            // remove from temp
            MarkAsUploaded(hash);
        }
        else
        {
            logger.LogDebug("File {FileName} already uploaded or being uploaded (hash: {Hash})", filePair.FullName, hash.ToShortString());
            await uploadTask;
        }

        // 4.Write the Pointer
        var pf = filePair.CreatePointerFile(hash);

        // 5. Write the PointerFileEntry
        handlerContext.StateRepository.UpsertPointerFileEntries(new PointerFileEntry
        {
            Hash             = hash,
            RelativeName     = pf.Path.FullName,
            CreationTimeUtc  = pf.CreationTime,
            LastWriteTimeUtc = pf.LastWriteTime
        });

        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 100, "Completed"));
    }

    private async Task UploadSmallFileAsync(HandlerContext handlerContext, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting small file TAR archive processing with boundary {SmallFileBoundary}", handlerContext.Request.SmallFileBoundary.Bytes().Humanize());
        
        InMemoryGzippedTarWriter tarWriter = null;

        try
        {
            await foreach (var filePairWithHash in hashedSmallFilesChannel.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (tarWriter is null)
                {
                    logger.LogDebug("Creating new TAR archive writer");
                    tarWriter = new InMemoryGzippedTarWriter(CompressionLevel.SmallestSize);
                }

                var (filePair, binaryHash) = filePairWithHash;

                // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
                var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, binaryHash);

                // 3. Upload the Binary, if needed
                if (needsToBeUploaded)
                {
                    var tarredEntry = await tarWriter.AddEntryAsync(filePair, binaryHash, cancellationToken);

                    logger.LogInformation("Added small file {FileName} to TAR queue (original: {OriginalSize}, archived: {ArchivedSize}, hash: {Hash})", filePair.FullName, filePair.BinaryFile.Length.Bytes().Humanize(), tarredEntry.ArchivedSize.Bytes().Humanize(), binaryHash.ToShortString());
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, "Queued in TAR..."));
                }
                else
                {
                    logger.LogInformation("Small file {FileName} already uploaded (hash: {Hash})", filePair.FullName, binaryHash.ToShortString());
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 100, "Already uploaded"));
                }

                var shouldProcessTar = (tarWriter.Position > handlerContext.Request.SmallFileBoundary ||
                     tarWriter.Position <= handlerContext.Request.SmallFileBoundary && hashedSmallFilesChannel.Reader.Completion.IsCompleted) && tarWriter.TarredEntries.Any();
                
                if (shouldProcessTar)
                {
                    logger.LogInformation("TAR archive size threshold reached ({TarSize}), processing archive with {FileCount} files", tarWriter.Position.Bytes().Humanize(), tarWriter.TarredEntries.Count);
                    await ProcessTarArchive(handlerContext, tarWriter, cancellationToken);

                    // Reset for next batch
                    tarWriter?.Dispose();
                    tarWriter = null;
                }
            }

            // Handle any remaining files in the final batch
            if (tarWriter?.TarredEntries.Any() == true)
            {
                logger.LogInformation("Processing final TAR archive with {FileCount} files", tarWriter.TarredEntries.Count);
                await ProcessTarArchive(handlerContext, tarWriter, cancellationToken);
            }
            
            logger.LogInformation("Small file TAR processing completed");
        }
        finally
        {
            // Ensure cleanup of resources
            tarWriter?.Dispose();
        }
    }

    private async Task ProcessTarArchive(HandlerContext handlerContext, InMemoryGzippedTarWriter tarWriter, CancellationToken cancellationToken)
    {
        var fileCount = tarWriter.TarredEntries.Count;
        var totalOriginalSize = tarWriter.TotalOriginalSize;
        
        logger.LogInformation("Processing TAR archive with {FileCount} files (total size: {TotalSize})", fileCount, totalOriginalSize.Bytes().Humanize());
        
        await using var sourceStream = tarWriter.GetCompletedArchive();

        var parentHash = await handlerContext.Hasher.GetHashAsync(sourceStream);
        sourceStream.Seek(0, SeekOrigin.Begin);
        
        logger.LogDebug("TAR archive hashed to {ParentHash}, uploading to storage", parentHash.ToShortString());

        foreach (var entry in tarWriter.TarredEntries)
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(entry.FilePair.FullName, 70, "Uploading TAR archive..."));

        // Upload the TAR archive
        var (totalOriginalSizeFromUpload, finalArchivedSize) = await UploadIfNotExistsAsync(
            handlerContext, parentHash, sourceStream, CompressionLevel.NoCompression /* The TAR file is already GZipped */, TarChunkContentType, cancellationToken);

        // Get the current tier (tier was already set in UploadIfNotExistsAsync)
        var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(parentHash, cancellationToken);
        var actualTier = properties?.StorageTier ?? handlerContext.Request.Tier;
        var compressionRatio = totalOriginalSize > 0 ? (double)finalArchivedSize / totalOriginalSize : 1.0;
        
        logger.LogInformation("TAR archive upload completed: {FileCount} files (original: {OriginalSize}, archived: {ArchivedSize}, compression: {CompressionRatio:P1}, tier: {StorageTier}, hash: {ParentHash})", 
            fileCount, totalOriginalSize.Bytes().Humanize(), finalArchivedSize.Bytes().Humanize(), compressionRatio, actualTier, parentHash.ToShortString());

        // Add BinaryProperties
        var tarBps = tarWriter.TarredEntries.Select(e => new BinaryProperties
        {
            Hash         = e.Hash,
            ParentHash   = parentHash,
            OriginalSize = e.FilePair.BinaryFile.Length,
            ArchivedSize = e.ArchivedSize,
            StorageTier  = actualTier
        });
        var parentBp = new BinaryProperties
        {
            Hash         = parentHash,
            OriginalSize = totalOriginalSize,
            ArchivedSize = finalArchivedSize,
            StorageTier  = actualTier
        };
        IEnumerable<BinaryProperties> bps = [.. tarBps, parentBp];
        handlerContext.StateRepository.AddBinaryProperties(bps.ToArray());

        // Mark as uploaded
        foreach (var entry in tarWriter.TarredEntries)
            MarkAsUploaded(entry.Hash);

        // 4.Write the Pointers
        var pfes = new List<PointerFileEntry>();
        foreach (var entry in tarWriter.TarredEntries)
        {
            var pf = entry.FilePair.CreatePointerFile(entry.Hash);
            pfes.Add(new PointerFileEntry
            {
                Hash             = entry.Hash,
                RelativeName     = pf.Path.FullName,
                CreationTimeUtc  = pf.CreationTimeUtc,
                LastWriteTimeUtc = pf.LastWriteTimeUtc
            });
        }

        // 5. Write the PointerFileEntry
        handlerContext.StateRepository.UpsertPointerFileEntries(pfes.ToArray());

        foreach (var entry in tarWriter.TarredEntries)
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(entry.FilePair.FullName, 100, "Archive complete"));
    }

    // -- UPLOAD STATUS HELPERS

    private (bool needsToBeUploaded, Task uploadTask) GetUploadStatus(HandlerContext handlerContext, Hash h)
    {
        var bp = handlerContext.StateRepository.GetBinaryProperty(h);

        lock (uploadingHashes)
        {
            if (bp is null)
            {
                if (uploadingHashes.TryGetValue(h, out var tcs))
                {
                    // Already uploading
                    return (false, tcs.Task);
                }
                else
                {
                    // To be uploaded
                    tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    uploadingHashes.Add(h, tcs);

                    return (true, tcs.Task);
                }
            }
            else
            {
                // Already uploaded
                return (false, Task.CompletedTask);
            }
        }
    }

    private void MarkAsUploaded(Hash h)
    {
        lock (uploadingHashes)
        {
            uploadingHashes.Remove(h, out var tcs);
            tcs.SetResult();
        }
    }
}