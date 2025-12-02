using Arius.Core.Shared.Concurrency;
using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using FluentResults;
using Humanizer;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Threading.Channels;
using Zio;

namespace Arius.Core.Features.Commands.Archive;

public abstract record ProgressUpdate;
public sealed record TaskProgressUpdate(string TaskName, double Percentage, string? StatusMessage = null) : ProgressUpdate;
public sealed record FileProgressUpdate(string FileName, double Percentage, string? StatusMessage = null) : ProgressUpdate;

internal class ArchiveCommandHandler : ICommandHandler<ArchiveCommand, Result<ArchiveCommandResult>>
{
    private readonly ILogger<ArchiveCommandHandler> logger;
    private readonly ILoggerFactory                 loggerFactory;
    private          int                            used;

    public ArchiveCommandHandler(ILogger<ArchiveCommandHandler> logger, ILoggerFactory loggerFactory, IOptions<AriusConfiguration> config)
    {
        this.logger        = logger;
        this.loggerFactory = loggerFactory;
    }

    // Orchestrates "only-one-uploader-per-hash" across all pipelines.
    // - Enter(hash) returns (isOwner, task):
    //   - Owner: first entrant for a given hash; responsible for doing the upload
    //   - Non-owner: anyone else for the same hash; must observe the owner's completion via task
    private readonly InFlightGate<Hash, Unit> uploadGate = new();

    // Statistics tracking
    private          int                   totalLocalFiles           = 0;
    private          int                   existingPointerFiles      = 0;
    private          int                   uniqueBinariesUploaded    = 0;
    private          int                   uniqueChunksUploaded      = 0;
    private          long                  bytesUploadedUncompressed = 0;
    private          long                  bytesUploadedCompressed   = 0;
    private          int                   pointerFilesCreated       = 0;
    private          int                   pointerFileEntriesDeleted = 0;
    private readonly ConcurrentBag<string> warnings                  = [];
    private          int                   filesSkipped              = 0;

    // Pipeline channels:
    //
    // 1) indexedFilesChannel:         producer = indexer (single),    consumers = hasher (parallel)
    // 2) hashedLargeFilesChannel:     producer = hasher (parallel),    consumers = large uploader (parallel)
    // 3) hashedSmallFilesChannel:     producer = hasher (parallel),    consumer  = small uploader (single)
    //
    // Notes:
    // - small files: a single consumer batches entries into a TAR; only the "owner" of a hash adds to the TAR.
    //   duplicates (non-owners) are *deferred* — we DO NOT block the reader.
    // - large files: each owner uploads the blob directly; non-owners await the owner (safe here because it's in a parallel consumer).
    private record FilePairWithHash(FilePair FilePair, Hash Hash);
    private readonly Channel<FilePair>         indexedFilesChannel     = ChannelExtensions.CreateBounded<FilePair>(capacity: 20, singleWriter: true, singleReader: false);
    private readonly Channel<FilePairWithHash> hashedLargeFilesChannel = ChannelExtensions.CreateBounded<FilePairWithHash>(capacity: 10, singleWriter: false, singleReader: false);
    private readonly Channel<FilePairWithHash> hashedSmallFilesChannel = ChannelExtensions.CreateBounded<FilePairWithHash>(capacity: 10, singleWriter: false, singleReader: true);


    // --- HANDLER

    public async ValueTask<Result<ArchiveCommandResult>> Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await new HandlerContextBuilder(request, loggerFactory)
            .BuildAsync();

        return await Handle(handlerContext, cancellationToken);
    }

    internal async ValueTask<Result<ArchiveCommandResult>> Handle(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        // Enforce single-use
        if (Interlocked.Exchange(ref used, 1) != 0)
            throw new InvalidOperationException($"{nameof(ArchiveCommandHandler)} can only be used once.");

        logger.LogInformation("Starting archive operation for path {LocalRoot} with hashing parallelism {HashingParallelism}, upload parallelism {UploadParallelism}", handlerContext.Request.LocalRoot, handlerContext.Request.HashingParallelism, handlerContext.Request.UploadParallelism);

        // Get chunk statistics BEFORE operation
        logger.LogDebug("Getting chunk statistics before archive operation");
        var statisticsBefore = await handlerContext.ArchiveStorage.GetChunkStatistics(cancellationToken);
        logger.LogInformation("Remote storage before: {ChunkCount} chunks, {BinaryCount} binaries, {ArchivedSize} bytes", statisticsBefore.ChunkCount, statisticsBefore.BinaryCount, statisticsBefore.ArchivedSize);

        using var errorCancellationTokenSource   = new CancellationTokenSource();
        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, errorCancellationTokenSource.Token);
        var       errorCancellationToken         = linkedCancellationTokenSource.Token;

        var tasks = new Dictionary<string, Task>
        {
            ["IndexTask"]            = CreateIndexTask(handlerContext, errorCancellationToken),
            ["HashTask"]             = CreateHashTask(handlerContext, errorCancellationToken),
            ["UploadLargeFilesTask"] = CreateUploadLargeFilesTask(handlerContext, errorCancellationToken),
            ["UploadSmallFilesTask"] = CreateUploadSmallFilesTarArchiveTask(handlerContext, errorCancellationToken)
        };

        foreach (var task in tasks.Values)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && !errorCancellationTokenSource.IsCancellationRequested)
                {
                    errorCancellationTokenSource.Cancel();
                }
            });
        }

        try
        {
            await Task.WhenAll(tasks.Values);

            // 6. Remove PointerFileEntries that do not exist on disk
            logger.LogDebug("Cleaning up pointer file entries that no longer exist on disk");
            pointerFileEntriesDeleted = handlerContext.StateRepository.DeletePointerFileEntries(pfe => !handlerContext.FileSystem.FileExists(pfe.RelativeName));

            // 7. Upload the new state file to blob storage
            string? newStateName = null;
            if (handlerContext.StateRepository.HasChanges)
            {
                var stateFileName = Path.GetFileNameWithoutExtension(handlerContext.StateRepository.StateDatabaseFile.Name);
                logger.LogInformation("Changes detected in database, uploading state file {StateFileName}", stateFileName);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Uploading state file...", 0));

                handlerContext.StateRepository.Vacuum();
                await handlerContext.ArchiveStorage.UploadStateAsync(stateFileName, handlerContext.StateRepository.StateDatabaseFile, cancellationToken);

                logger.LogInformation("Successfully uploaded state file {StateFileName}", stateFileName);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Uploading state file...", 100, "Completed"));
                newStateName = stateFileName;
            }
            else
            {
                logger.LogInformation("No changes to the database. Skipping upload and deleting local state file.");
                handlerContext.StateRepository.Delete();
            }

            // Get chunk statistics AFTER operation
            logger.LogDebug("Getting chunk statistics after archive operation");
            var statisticsAfter = await handlerContext.ArchiveStorage.GetChunkStatistics(cancellationToken);
            logger.LogInformation("Remote storage after: {ChunkCount} chunks, {BinaryCount} binaries, {ArchivedSize} bytes", statisticsAfter.ChunkCount, statisticsAfter.BinaryCount, statisticsAfter.ArchivedSize);

            logger.LogInformation("Archive operation completed successfully for path {LocalRoot}", handlerContext.Request.LocalRoot);

            return Result.Ok(new ArchiveCommandResult
            {
                TotalLocalFiles      = totalLocalFiles,
                ExistingPointerFiles = existingPointerFiles,

                ChunksBeforeOperation        = statisticsBefore.ChunkCount,
                BinariesBeforeOperation      = statisticsBefore.BinaryCount,
                ArchivedSizeBeforeOperation  = statisticsBefore.ArchivedSize,

                UniqueBinariesUploaded    = uniqueBinariesUploaded,
                UniqueChunksUploaded      = uniqueChunksUploaded,
                BytesUploadedUncompressed = bytesUploadedUncompressed,
                BytesUploadedCompressed   = bytesUploadedCompressed,
                PointerFilesCreated       = pointerFilesCreated,
                PointerFileEntriesDeleted = pointerFileEntriesDeleted,

                ChunksAfterOperation        = statisticsAfter.ChunkCount,
                BinariesAfterOperation      = statisticsAfter.BinaryCount,
                ArchivedSizeAfterOperation  = statisticsAfter.ArchivedSize,

                NewStateName              = newStateName,
                Warnings                  = warnings.ToArray(),
                FilesSkipped              = filesSkipped
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !errorCancellationTokenSource.IsCancellationRequested)
        {
            // User-triggered cancellation
            logger.LogInformation("Archive operation cancelled by user");
            return Result.Fail("Archive operation was cancelled by user");
        }
        catch (Exception ex)
        {
            // Either a task failed with an exception or error-triggered cancellation occurred

            var faultedTasks = tasks.Where(kvp => kvp.Value.IsFaulted).Select(kvp => (Name: kvp.Key, Exception: kvp.Value.Exception!.GetBaseException())).ToArray();

            // Trigger error-driven cancellation to signal other tasks to stop gracefully
            errorCancellationTokenSource.Cancel();

            // Wait for all tasks to complete gracefully
            await Task.WhenAll(tasks.Values.Select(async t =>
            {
                try { await t; }
                catch { /* Ignore exceptions during graceful shutdown */ }
            }));

            // Observe all task exceptions to prevent UnobservedTaskException
            foreach (var task in tasks.Values.Where(t => t.IsFaulted))
            {
                _ = task.Exception;
            }

            // Log cancelled tasks (debug level)
            var cancelledTaskNames = tasks.Where(kvp => kvp.Value.IsCanceled).Select(kvp => kvp.Key).ToArray();
            if (cancelledTaskNames.Any())
            {
                logger.LogDebug("Tasks cancelled during graceful shutdown: {TaskNames}", string.Join(", ", cancelledTaskNames));
            }

            // Log and handle failed tasks (error level)
            if (faultedTasks is { Length: 1 } && faultedTasks.Single() is var faultedTask)
            {
                // Single faulted task - return the exception
                var msg = faultedTask.Exception?.GetBaseException().Message ?? "UNKNOWN";
                logger.LogError(faultedTask.Exception, "Task '{TaskName}' failed with exception '{Exception}'", faultedTask.Name, msg);
                return Result.Fail($"Archive operation failed: {faultedTask.Name} failed with {msg}").WithError(new ExceptionalError(faultedTask.Exception));
            }
            else
            {
                // Multiple faulted tasks - return aggregate exception
                var exceptions         = faultedTasks.Select(ft => ft.Exception).ToArray();
                var aggregateException = new AggregateException("Multiple tasks failed during archive operation", exceptions);
                var faultedTaskNames = string.Join(", ", faultedTasks.Select(ft => ft.Name));
                logger.LogError(aggregateException, "Tasks failed: {FaultedTaskNames}", faultedTaskNames);
                return Result.Fail($"Archive operation failed: {faultedTaskNames} tasks failed").WithError(new ExceptionalError(aggregateException));
            }
        }
    }


    // --- HIGH LEVEL TASKS

    private Task CreateIndexTask(HandlerContext handlerContext, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            try
            {
                logger.LogInformation("Starting file indexing in path {LocalRoot}", handlerContext.Request.LocalRoot);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Indexing files...", 0));

                foreach (var fp in handlerContext.FileSystem.EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref totalLocalFiles);
                    await indexedFilesChannel.Writer.WriteAsync(FilePair.FromBinaryFileFileEntry(fp), cancellationToken);
                }

                logger.LogInformation("File indexing completed: found {FileCount} files in {LocalRoot}", totalLocalFiles, handlerContext.Request.LocalRoot);
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Indexing files...", 100, $"Found {totalLocalFiles} files"));
            }
            catch (OperationCanceledException)
            {
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Indexing files...", -1, $"Cancelled"));
                logger.LogDebug("File indexing cancelled");
                throw;
            }
            catch (Exception e) // TODO Align with approach of HashTask where we skip the file and log a warning instead of failing the entire task, write test Error_IndexTaskFails_ShouldSkipProblematicFileAndContinue
            {
                logger.LogError(e, "File indexing failed with exception");
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate("Indexing files...", -1, $"Error"));
                throw;
            }
            finally
            {
                indexedFilesChannel.Writer.Complete();
                logger.LogDebug("Index channel completed");
            }
        });  // No cancellation token passed to Task.Run, this allows proper catch/finally execution

    private Task CreateHashTask(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting file hashing with parallelism {HashingParallelism}", handlerContext.Request.HashingParallelism);

        var t = Parallel.ForEachAsync(indexedFilesChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions { MaxDegreeOfParallelism = handlerContext.Request.HashingParallelism, CancellationToken = cancellationToken },
            async (filePair, innerCancellationToken) =>
            {
                try
                {
                    // Track if pointer file already exists
                    if (filePair.PointerFile.Exists)
                        Interlocked.Increment(ref existingPointerFiles);

                    var fileSizeFormatted = filePair.ExistingBinaryFile?.Length.Bytes().Humanize() ?? "0 B";
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 10, $"Hashing {fileSizeFormatted}..."));

                    if (filePair.Type == FilePairType.PointerFileOnly)
                    {
                        // The pointer does not have a binary (yet) -- this is an edge case eg when re-uploading an entire archive
                        // TODO implement 'var latentPointers = new ConcurrentQueue<PointerFile>();'

                        logger.LogWarning("File {FileName} is a pointer file without an associated binary, skipping", filePair.FullName);
                        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, -1, "WARN: pointer file without binary"));

                        var warningMessage = $"File '{filePair.FullName}' is a pointer file without an associated binary, skipping";
                        warnings.Add(warningMessage);
                        Interlocked.Increment(ref filesSkipped);
                    }
                    else
                    {
                        // 1. Hash the file
                        var h = await handlerContext.Hasher.GetHashAsync(filePair, innerCancellationToken);

                        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 50, "Waiting for upload..."));

                        var isSmallFile = filePair.BinaryFile.Length <= handlerContext.Request.SmallFileBoundary;
                        logger.LogDebug("File {FileName} hashed to {Hash}, routing to {FileType} processing (size: {FileSize})", filePair.FullName, h.ToShortString(), isSmallFile ? "small" : "large", fileSizeFormatted);

                        if (isSmallFile)
                            await hashedSmallFilesChannel.Writer.WriteAsync(new(filePair, h), cancellationToken: innerCancellationToken);
                        else
                            await hashedLargeFilesChannel.Writer.WriteAsync(new(filePair, h), cancellationToken: innerCancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, -1, "Cancelled..."));
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogWarning("Error when hashing file {FileName}: {Message}, skipping.", filePair.FullName, e.Message);
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, -1, $"Error: {e.Message}"));

                    var warningMessage = $"Error when hashing file '{filePair.FullName}': {e.Message}, skipping";
                    warnings.Add(warningMessage);
                    Interlocked.Increment(ref filesSkipped);
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

    private Task CreateUploadLargeFilesTask(HandlerContext handlerContext, CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(hashedLargeFilesChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions { MaxDegreeOfParallelism = handlerContext.Request.UploadParallelism, CancellationToken = cancellationToken },
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
                catch (Exception e) // TODO Align with approach of HashTask where we skip the file and log a warning instead of failing the entire task, update test Error_UploadTaskFails_ShouldReturnFailure
                {
                    logger.LogError(e, "Large file upload task failed");
                    throw;
                }
            });

    private Task CreateUploadSmallFilesTarArchiveTask(HandlerContext handlerContext, CancellationToken cancellationToken) =>
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
            catch (Exception e)  // TODO Align with approach of HashTask where we skip the file and log a warning instead of failing the entire task, write a test like Error_HashTaskFails_ShouldSkipProblematicFileAndContinue
            {
                logger.LogError(e, "Small files TAR archive task failed");
                throw;
            }
        }); // No cancellation token passed to Task.Run, this allows proper catch/finally execution


    // --- HELPERS


    internal async Task<(long OriginalSize, long ArchivedSize)> UploadIfNotExistsAsync(HandlerContext handlerContext, Hash hash, Stream sourceStream, CompressionLevel compressionLevel, string contentType, Dictionary<string, string>? additionalMetadata = null, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Attempting to upload chunk with hash {Hash} using content type {ContentType}", hash.ToShortString(), contentType);

        // Try to open the blob for writing WITHOUT overwriting it
        var targetStreamResult = await handlerContext.ArchiveStorage.OpenWriteChunkAsync(hash, compressionLevel, contentType, progress: null, overwrite: false, cancellationToken: cancellationToken);

        if (targetStreamResult.IsSuccess)
        {
            logger.LogDebug("Chunk does not exist, performing new upload for hash {Hash}", hash.ToShortString());

            // New upload - perform the upload
            long originalSize;
            await using (var targetStream = targetStreamResult.Value)
            {
                await sourceStream.CopyToAsync(targetStream, bufferSize: 81920 /* todo optimize */, cancellationToken);
                await targetStream.FlushAsync(cancellationToken);

                originalSize = sourceStream.Position;
                // note targetStream.Position is off by a few bytes and does not accurately reflect the size in blob storage
            }

            var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, cancellationToken);

            // Write Metadata
            var metadata = new Dictionary<string, string>(
            [
                new("OriginalContentLength", originalSize.ToString()),
                ..additionalMetadata ?? []
            ]);

            await handlerContext.ArchiveStorage.SetChunkMetadataAsync(hash, metadata);

            // Ensure correct storage tier
            await handlerContext.ArchiveStorage.SetChunkStorageTierPerPolicy(hash, properties.ContentLength, handlerContext.Request.Tier);

            logger.LogDebug("Upload completed for hash {Hash}: original={OriginalSize}, archived={ArchivedSize}", hash.ToShortString(), originalSize, properties.ContentLength);

            return (originalSize, properties.ContentLength);
        }
        else if (targetStreamResult.HasError<BlobAlreadyExistsError>())
        {
            logger.LogInformation("Chunk already exists for hash {Hash}, checking content type", hash.ToShortString());

            // Blob exists - check content type
            var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, cancellationToken);

            if (properties?.ContentType == contentType && properties.Metadata != null &&
                                                          properties.Metadata.TryGetValue("OriginalContentLength", out var originalSizeStr) &&
                                                          long.TryParse(originalSizeStr, out var originalSize))
            {
                // Correct content type: file was already uploaded previous time --> read from metadata
                logger.LogInformation("Using existing metadata for hash {Hash}: original={OriginalSize}, archived={ArchivedSize}", hash.ToShortString(), originalSize, properties.ContentLength);

                // Ensure correct storage tier
                await handlerContext.ArchiveStorage.SetChunkStorageTierPerPolicy(hash, properties.ContentLength, handlerContext.Request.Tier);

                return (originalSize, properties.ContentLength);
            }

            // Incorrect content type or metadata not set: file was not properly uploaded last time --> delete and re-upload
            logger.LogWarning("Chunk exists with incorrect metadata format for hash {Hash}, deleting and re-uploading", hash.ToShortString());

            await handlerContext.ArchiveStorage.DeleteChunkAsync(hash, cancellationToken);

            // Recursive call to upload
            return await UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, contentType, additionalMetadata, cancellationToken);
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

        // LARGE FILES
        // The first thread to claim 'hash' (owner) uploads the binary now. anyone else (non-owner) simply awaits the owner
        var bp = handlerContext.StateRepository.GetBinaryProperty(hash);
        if (bp is null)
        {
            var (isOwner, uploadTask) = uploadGate.Enter(hash);
            if (isOwner)
            {
                // OWNER (large): perform the actual upload, then Complete(hash)
                try
                {
                    var fileSizeFormatted = filePair.ExistingBinaryFile?.Length.Bytes().Humanize() ?? "0 B";
                    logger.LogInformation("Uploading large file {FileName} ({FileSize}, hash: {Hash})", filePair.FullName, fileSizeFormatted, hash.ToShortString());
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, $"Uploading {fileSizeFormatted}..."));

                    // Upload
                    await using var sourceStream = filePair.BinaryFile.OpenRead();
                    var (sourceStreamPosition, targetStreamPosition) = await UploadIfNotExistsAsync(handlerContext, hash, sourceStream, CompressionLevel.SmallestSize, ChunkContentTypes.ChunkContentType, null, cancellationToken);

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

                    Interlocked.Increment(ref uniqueBinariesUploaded);
                    Interlocked.Increment(ref uniqueChunksUploaded);
                    Interlocked.Add(ref bytesUploadedUncompressed, sourceStreamPosition);
                    Interlocked.Add(ref bytesUploadedCompressed, targetStreamPosition);

                    uploadGate.Complete(hash, Unit.Value);
                }
                catch (OperationCanceledException)
                {
                    uploadGate.Cancel(hash, cancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    uploadGate.Fault(hash, ex);
                    throw;
                }
            }
            else
            {
                // NON-OWNER (large): wait for the owner to finish this hash
                await uploadTask;
            }
        }

        // 4.Write the Pointer
        var pf = WritePointerFile(filePair, hash);

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

        // Duplicates (non-owners) are *deferred* here:
        // - We NEVER await the gate in the loop (that would block the single reader and risk deadlock).
        // - Instead, we attach continuations to the owner's task and flush them after TAR batches are done.
        var deferredPointerWrites = new List<Task>();

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

                // SMALL FILES
                // Rule: if BinaryProperties do not yet exist:
                //   - OWNER (first entrant for hash): enqueue the file into the in-memory TAR.
                //   - NON-OWNER: DO NOT block; create a deferred task to write the pointer once the owner completes.
                var bp = handlerContext.StateRepository.GetBinaryProperty(binaryHash);
                if (bp is null)
                {
                    var (isOwner, uploadTask) = uploadGate.Enter(binaryHash);

                    if (isOwner)
                    {
                        // OWNER (small): stage entry into TAR; the actual upload happens when we flush the TAR.
                        var tarredEntry = await tarWriter.AddEntryAsync(filePair, binaryHash, cancellationToken);
                        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, "Queued in TAR..."));
                        logger.LogInformation("Added small file {FileName} to TAR queue (original: {OriginalSize}, archived: {ArchivedSize}, hash: {Hash})", filePair.FullName, filePair.BinaryFile.Length.Bytes().Humanize(), tarredEntry.ArchivedSize.Bytes().Humanize(), binaryHash.ToShortString());

                        // Flush TAR when:
                        // - accumulated TAR size exceeds boundary, OR
                        // - channel is completed (no more input) and we still have staged entries
                        var shouldProcessTar =
                            (tarWriter.Position > handlerContext.Request.SmallFileBoundary ||
                             (tarWriter.Position <= handlerContext.Request.SmallFileBoundary && hashedSmallFilesChannel.Reader.Completion.IsCompleted))
                            && tarWriter.TarredEntries.Any();

                        if (shouldProcessTar)
                        {
                            // Process TAR: upload parent TAR blob, add BinaryProperties for child + parent,
                            // write pointer entries (OWNERS ONLY), then Complete(hash) for each owner entry.
                            logger.LogInformation("TAR archive size threshold reached ({TarSize}), processing archive with {FileCount} files", tarWriter.Position.Bytes().Humanize(), tarWriter.TarredEntries.Count);

                            try
                            {
                                await ProcessTarArchive(handlerContext, tarWriter, cancellationToken);
                                foreach (var entry in tarWriter.TarredEntries)
                                    uploadGate.Complete(entry.Hash, Unit.Value);
                            }
                            catch (OperationCanceledException)
                            {
                                foreach (var entry in tarWriter.TarredEntries)
                                    uploadGate.Cancel(entry.Hash, cancellationToken);
                                throw;
                            }
                            catch (Exception ex)
                            {
                                foreach (var entry in tarWriter.TarredEntries)
                                    uploadGate.Fault(entry.Hash, ex);
                                throw;
                            }

                            // Reset for next batch
                            tarWriter?.Dispose();
                            tarWriter = null;
                        }
                    }
                    else
                    {
                        // NON-OWNER (small):
                        // Do NOT await the gate here (single reader!). Defer pointer write until owner completes.
                        // By the time this continuation runs, owner has uploaded & inserted BinaryProperties
                        var deferred = uploadTask.ContinueWith(t =>
                        {
                            if (t.IsFaulted) throw t.Exception!.GetBaseException();
                            if (t.IsCanceled) throw new OperationCanceledException();

                            var pf = WritePointerFile(filePair, binaryHash);

                            handlerContext.StateRepository.UpsertPointerFileEntries(new PointerFileEntry
                            {
                                Hash             = binaryHash,
                                RelativeName     = pf.Path.FullName,
                                CreationTimeUtc  = pf.CreationTimeUtc,
                                LastWriteTimeUtc = pf.LastWriteTimeUtc
                            });

                            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 100, "Completed (duplicate)"));
                        }, TaskContinuationOptions.ExecuteSynchronously);

                        deferredPointerWrites.Add(deferred);
                    }
                }
                else
                {
                    // Already uploaded
                    logger.LogInformation("Small file {FileName} already uploaded (hash: {Hash})", filePair.FullName, binaryHash.ToShortString());

                    var pf = WritePointerFile(filePair, binaryHash);

                    handlerContext.StateRepository.UpsertPointerFileEntries(new PointerFileEntry
                    {
                        Hash             = binaryHash,
                        RelativeName     = pf.Path.FullName,
                        CreationTimeUtc  = pf.CreationTimeUtc,
                        LastWriteTimeUtc = pf.LastWriteTimeUtc
                    });

                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 100, "Already uploaded"));
                }
            }

            // Final partial TAR flush (owners only)
            if (tarWriter?.TarredEntries.Any() == true)
            {
                try
                {
                    await ProcessTarArchive(handlerContext, tarWriter, cancellationToken);
                    foreach (var entry in tarWriter.TarredEntries)
                        uploadGate.Complete(entry.Hash, Unit.Value);
                }
                catch (OperationCanceledException)
                {
                    foreach (var entry in tarWriter.TarredEntries)
                        uploadGate.Cancel(entry.Hash, cancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    foreach (var entry in tarWriter.TarredEntries)
                        uploadGate.Fault(entry.Hash, ex);
                    throw;
                }
            }

            // Now that all owners have completed, flush deferred duplicate pointers.
            if (deferredPointerWrites.Count > 0)
                await Task.WhenAll(deferredPointerWrites);
            
            logger.LogInformation("Small file TAR processing completed");
        }
        finally
        {
            // Ensure cleanup of resources
            tarWriter?.Dispose();
        }
    }

    private PointerFile WritePointerFile(FilePair filePair, Hash hash)
    {
        if (!filePair.PointerFile.Exists) // NOTE: this is semi-h4x0r; we could have CreatePointerFile return a value whether it has created the file or not, or not write the pointerfile if it already exists, but just writing it anyway is cheap & defensive
            Interlocked.Increment(ref pointerFilesCreated);
        return filePair.CreatePointerFile(hash);
    }

    private async Task ProcessTarArchive(HandlerContext handlerContext, InMemoryGzippedTarWriter tarWriter, CancellationToken cancellationToken)
    {
        // OWNER ENTRIES ONLY:
        // - Upload the parent TAR blob (already gzipped)
        // - Insert BinaryProperties for each child entry + parent TAR
        // - Write pointer entries for owners (duplicates are handled by deferred tasks outside)
        // - Progress for owners goes to 100% here; duplicates finalize via deferred tasks
        var fileCount = tarWriter.TarredEntries.Count;
        var totalOriginalSize = tarWriter.TotalOriginalSize;

        logger.LogInformation("Processing TAR archive with {FileCount} files (total size: {ArchivedSize})", fileCount, totalOriginalSize.Bytes().Humanize());
        
        await using var sourceStream = tarWriter.GetCompletedArchive();

        var parentHash = await handlerContext.Hasher.GetHashAsync(sourceStream, cancellationToken);
        sourceStream.Seek(0, SeekOrigin.Begin);
        
        logger.LogDebug("TAR archive hashed to {ParentHash}, uploading to storage", parentHash.ToShortString());

        foreach (var entry in tarWriter.TarredEntries)
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(entry.FilePair.FullName, 70, "Uploading TAR archive..."));

        // Upload the TAR archive
        var tarMetadata = new Dictionary<string, string>
        {
            ["SmallChunkCount"] = fileCount.ToString()
        };
        var (totalOriginalSizeFromUpload, finalArchivedSize) = await UploadIfNotExistsAsync(handlerContext, parentHash, sourceStream, CompressionLevel.NoCompression /* The TAR file is already GZipped */, ChunkContentTypes.TarChunkContentType, tarMetadata, cancellationToken);

        // Get the current tier (tier was already set in UploadIfNotExistsAsync)
        var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(parentHash, cancellationToken);
        var actualTier = properties?.StorageTier ?? handlerContext.Request.Tier;
        var compressionRatio = totalOriginalSize > 0 ? (double)finalArchivedSize / totalOriginalSize : 1.0;
        
        logger.LogInformation("TAR archive upload completed: {FileCount} files (original: {OriginalSize}, archived: {ArchivedSize}, compression: {CompressionRatio:P1}, tier: {StorageTier}, hash: {ParentHash})", fileCount, totalOriginalSize.Bytes().Humanize(), finalArchivedSize.Bytes().Humanize(), compressionRatio, actualTier, parentHash.ToShortString());

        Interlocked.Add(ref uniqueBinariesUploaded, fileCount);
        Interlocked.Increment(ref uniqueChunksUploaded);
        Interlocked.Add(ref bytesUploadedUncompressed, totalOriginalSize);
        Interlocked.Add(ref bytesUploadedCompressed, finalArchivedSize);

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


        // 4.Write the Pointers
        var pfes = new List<PointerFileEntry>();
        foreach (var entry in tarWriter.TarredEntries)
        {
            var pf = entry.FilePair.CreatePointerFile(entry.Hash);
            Interlocked.Increment(ref pointerFilesCreated);

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
}
