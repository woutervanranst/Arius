using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Humanizer;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Formats.Tar;
using System.IO.Compression;
using System.Threading.Channels;
using WouterVanRanst.Utils.Extensions;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Commands;

public record ProgressUpdate;
public record TaskProgressUpdate(string TaskName, double Percentage, string? StatusMessage = null) : ProgressUpdate;
public record FileProgressUpdate(string FileName, double Percentage, string? StatusMessage = null) : ProgressUpdate;

internal class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{
    private readonly ILogger<ArchiveCommandHandler> logger;
    private readonly IOptions<AriusConfiguration>   config;

    public ArchiveCommandHandler(
        ILogger<ArchiveCommandHandler> logger,
        IOptions<AriusConfiguration> config)
    {
        this.logger = logger;
        this.config = config;
    }

    private readonly Dictionary<Hash, TaskCompletionSource> uploadingHashes = new();

    private readonly Channel<FilePair>         indexedFilesChannel     = GetBoundedChannel<FilePair>(capacity: 20,        singleWriter: true, singleReader: false);
    private readonly Channel<FilePairWithHash> hashedLargeFilesChannel = GetBoundedChannel<FilePairWithHash>(capacity: 10, singleWriter: false, singleReader: false);
    // private readonly Channel<FilePairWithHash> hashedSmallFilesChannel = Channel.CreateUnbounded<FilePairWithHash>(new UnboundedChannelOptions() { AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = true }); // unbounded since there can be a deadlock 
    private readonly Channel<FilePairWithHash> hashedSmallFilesChannel = GetBoundedChannel<FilePairWithHash>(capacity: 10, singleWriter: false, singleReader: true);

    private record FilePairWithHash(FilePair FilePair, Hash Hash);

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        var newVersion     = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss.fffZ");
        var handlerContext = await HandlerContext.CreateAsync(request, newVersion);

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
            handlerContext.StateRepo.DeletePointerFileEntries(pfe => !handlerContext.FileSystem.FileExists(pfe.RelativeName));

            // 7. Upload the StateRepository if there are changes
            if (handlerContext.StateRepo.HasChanges)
            {
                logger.LogInformation("Uploading StateRepository to blob storage...");
                await handlerContext.StateRepositoryCache.UploadLocalCacheAsync(handlerContext.Version, cancellationToken);
                logger.LogInformation("StateRepository uploaded successfully.");
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
    }

    private Task CreateIndexTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Task.Run(async () =>
        {
            try
            {
                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate($"Indexing '{handlerContext.Request.LocalRoot}'...", 0));

                int fileCount = 0;
                foreach (var fp in handlerContext.FileSystem.EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref fileCount);
                    await indexedFilesChannel.Writer.WriteAsync(FilePair.FromBinaryFileFileEntry(fp), cancellationToken);
                }

                indexedFilesChannel.Writer.Complete();

                handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate($"Indexing '{handlerContext.Request.LocalRoot}'...", 100, $"Found {fileCount} files"));
            }
            catch (OperationCanceledException)
            {
                indexedFilesChannel.Writer.Complete();
                throw;
            }
            catch (Exception)
            {
                indexedFilesChannel.Writer.Complete();
                errorCancellationTokenSource.Cancel();
                throw;
            }
        }, cancellationToken);

    private Task CreateHashTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource)
    {
        var t = Parallel.ForEachAsync(indexedFilesChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions { MaxDegreeOfParallelism = handlerContext.Request.Parallelism, CancellationToken = cancellationToken },
            async (filePair, innerCancellationToken) =>
            {
                try
                {
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 10, $"Hashing {filePair.ExistingBinaryFile?.Length.Bytes().Humanize()} ..."));

                    // 1. Hash the file
                    var h = await handlerContext.Hasher.GetHashAsync(filePair);

                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 50, $"Waiting for upload..."));

                    if (filePair.BinaryFile.Length <= handlerContext.Request.SmallFileBoundary)
                        await hashedSmallFilesChannel.Writer.WriteAsync(new(filePair, h), cancellationToken: innerCancellationToken);
                    else
                        await hashedLargeFilesChannel.Writer.WriteAsync(new(filePair, h), cancellationToken: innerCancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    errorCancellationTokenSource.Cancel();
                    throw;
                }
            });

        t.ContinueWith(_ =>
        {
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
                catch (Exception)
                {
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
            catch (Exception)
            {
                errorCancellationTokenSource.Cancel();
                throw;
            }
        }, cancellationToken);

    private const string ChunkContentType = "application/aes256cbc+gzip";
    private const string TarChunkContentType = "application/aes256cbc+tar+gzip";


    private async Task UploadLargeFileAsync(HandlerContext handlerContext, FilePairWithHash filePairWithHash, CancellationToken cancellationToken = default)
    {
        var (filePair, hash) = filePairWithHash;

        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
        var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, hash);

        // 3. Upload the Binary, if needed
        if (needsToBeUploaded)
        {
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, $"Uploading {filePair.ExistingBinaryFile?.Length.Bytes().Humanize()}..."));

            // Upload
            await using var blobStream = await handlerContext.BlobStorage.OpenWriteChunkAsync(
                h: hash,
                contentType: ChunkContentType,
                metadata: null,
                progress: null,
                cancellationToken: cancellationToken);
            await using var cryptoStream = await blobStream.GetCryptoStreamAsync(handlerContext.Request.Passphrase, cancellationToken);
            await using var gzs          = new GZipStream(cryptoStream, CompressionLevel.Optimal);

            await using var ss = filePair.BinaryFile.OpenRead();
            await ss.CopyToAsync(gzs, bufferSize: 81920, cancellationToken);

            // Flush all buffers
            await gzs.FlushAsync(cancellationToken);
            await cryptoStream.FlushAsync(cancellationToken);
            await blobStream.FlushAsync(cancellationToken);

            // Update tier
            var actualTier = await handlerContext.BlobStorage.SetChunkStorageTierPerPolicy(hash, blobStream.Position, handlerContext.Request.Tier);

            // Add to db
            handlerContext.StateRepo.AddBinaryProperties(new BinaryPropertiesDto
            {
                Hash         = hash,
                OriginalSize = ss.Position,
                ArchivedSize = blobStream.Position,
                StorageTier  = actualTier
            });

            // remove from temp
            MarkAsUploaded(hash);
        }
        else
        {
            await uploadTask;
        }

        // 4.Write the Pointer
        var pf = filePair.GetOrCreatePointerFile(hash);

        // 5. Write the PointerFileEntry
        handlerContext.StateRepo.UpsertPointerFileEntries(new PointerFileEntryDto
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
        MemoryStream ms = null;
        TarWriter tarWriter = null;
        GZipStream gzip = null;
        List<(FilePair FilePair, Hash Hash, long ArchivedSize)> tarredFilePairs = new();
        long originalSize = 0;
        long previousPosition = 0;

        try
        {
            await foreach (var filePairWithHash in hashedSmallFilesChannel.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (tarWriter is null)
                {
                    ms = new MemoryStream();
                    gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true);
                    tarWriter = new TarWriter(gzip);
                    originalSize = 0;

                    await gzip.FlushAsync(cancellationToken);
                    previousPosition = ms.Position;
                }

                var (filePair, binaryHash) = filePairWithHash;

                // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
                var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, binaryHash);

                // 3. Upload the Binary, if needed
                if (needsToBeUploaded)
                {
                    var fn = handlerContext.FileSystem.ConvertPathToInternal(filePair.Path);
                    await tarWriter.WriteEntryAsync(fn, binaryHash.ToString(), cancellationToken);

                    await gzip.FlushAsync(cancellationToken);
                    await ms.FlushAsync(cancellationToken);

                    originalSize += filePair.BinaryFile.Length;
                    var archivedSize = ms.Position - previousPosition;

                    tarredFilePairs.Add((filePair, binaryHash, archivedSize));
                    previousPosition = ms.Position;

                    logger.LogInformation($"Added '{filePair.FullName}' ({filePair.BinaryFile.Length.Bytes()} to {archivedSize.Bytes()}) to TAR queue");
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, $"Queued in TAR..."));
                }
                else
                {
                    logger.LogInformation($"Binary for '{filePair.FullName}' ({binaryHash.ToShortString()}) already uploaded");
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 100, $"Already uploaded"));
                }

                if ((ms.Position > 1024 * 1024 ||
                     (ms.Position <= 1024 * 1024 && hashedSmallFilesChannel.Reader.Completion.IsCompleted)) && tarredFilePairs.Any())
                {
                    logger.LogInformation($"Uploading TAR");
                    await ProcessTarArchive(handlerContext, ms, gzip, tarWriter, tarredFilePairs, originalSize, cancellationToken);

                    // Reset for next batch
                    ms?.Dispose();
                    tarWriter = null;
                    ms = null;
                    gzip = null;
                    tarredFilePairs.Clear();
                    originalSize = 0;
                    previousPosition = 0;
                }
            }

            // Handle any remaining files in the final batch
            if (tarredFilePairs.Any() && ms != null)
            {
                await ProcessTarArchive(handlerContext, ms, gzip, tarWriter, tarredFilePairs, originalSize, cancellationToken);
            }
        }
        finally
        {
            // Ensure cleanup of resources
            tarWriter?.Dispose();
            gzip?.Dispose();
            ms?.Dispose();
        }
    }

    private async Task ProcessTarArchive(HandlerContext handlerContext, MemoryStream ms, GZipStream gzip, TarWriter tarWriter, List<(FilePair FilePair, Hash Hash, long ArchivedSize)> tarredFilePairs, long originalSize, CancellationToken cancellationToken)
    {
        tarWriter.Dispose();
        gzip.Dispose();

        ms.Seek(0, SeekOrigin.Begin);

        var tarHash = await handlerContext.Hasher.GetHashAsync(ms);

        File.WriteAllBytes($@"C:\Users\WouterVanRanst\Downloads\TempTars\{tarHash}.tar.gzip", ms.ToArray());

        ms.Seek(0, SeekOrigin.Begin);

        foreach (var (tarredFilePair, _, _) in tarredFilePairs)
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(tarredFilePair.FullName, 70, $"Uploading TAR..."));

        await using var blobStream = await handlerContext.BlobStorage.OpenWriteChunkAsync(
            h: tarHash,
            contentType: TarChunkContentType,
            metadata: null,
            progress: null,
            cancellationToken: cancellationToken);
        await using var cryptoStream = await blobStream.GetCryptoStreamAsync(handlerContext.Request.Passphrase, cancellationToken);
        await ms.CopyToAsync(cryptoStream, bufferSize: 1024 * 1024 * 2, cancellationToken);

        // Flush all buffers
        await cryptoStream.FlushAsync(cancellationToken);
        await blobStream.FlushAsync(cancellationToken);

        // Update tier
        var actualTier = await handlerContext.BlobStorage.SetChunkStorageTierPerPolicy(tarHash, blobStream.Position, handlerContext.Request.Tier);

        // Add BinaryProperties
        var bps = tarredFilePairs.Select(x => new BinaryPropertiesDto
        {
            Hash         = x.Hash,
            ParentHash   = tarHash,
            OriginalSize = x.FilePair.BinaryFile.Length,
            ArchivedSize = x.ArchivedSize,
            StorageTier  = actualTier
        }).ToArray();
        handlerContext.StateRepo.AddBinaryProperties(bps);

        handlerContext.StateRepo.AddBinaryProperties(new BinaryPropertiesDto
        {
            Hash         = tarHash,
            OriginalSize = originalSize,
            ArchivedSize = blobStream.Position,
            StorageTier  = actualTier
        });

        // Mark as uploaded
        foreach (var (_, binaryHash2, _) in tarredFilePairs)
            MarkAsUploaded(binaryHash2);

        // 4.Write the Pointers
        var pfes = new List<PointerFileEntryDto>();
        foreach (var (filePair22, binaryHash22, _) in tarredFilePairs)
        {
            var pf = filePair22.GetOrCreatePointerFile(binaryHash22);
            pfes.Add(new PointerFileEntryDto
            {
                Hash             = binaryHash22,
                RelativeName     = pf.Path.FullName,
                CreationTimeUtc  = pf.CreationTime,
                LastWriteTimeUtc = pf.LastWriteTime
            });
        }

        // 5. Write the PointerFileEntry
        handlerContext.StateRepo.UpsertPointerFileEntries(pfes.ToArray());

        foreach (var (tarredFilePair, _, _) in tarredFilePairs)
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(tarredFilePair.FullName, 100, $"TAR Complete"));
    }

    // -- UPLOAD STATUS HELPERS

    private (bool needsToBeUploaded, Task uploadTask) GetUploadStatus(HandlerContext handlerContext, Hash h)
    {
        var bp = handlerContext.StateRepo.GetBinaryProperty(h);

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

    private static Channel<T> GetBoundedChannel<T>(int capacity, bool singleWriter, bool singleReader)
        => Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
            SingleWriter = singleWriter,
            SingleReader = singleReader
        });

    //private static ParallelOptions GetParallelOptions(int maxDegreeOfParallelism, CancellationToken cancellationToken = default)
    //    => new() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };

    private class HandlerContext
    {
        public static async Task<HandlerContext> CreateAsync(ArchiveCommand request, string version)
        {
            var bs  = await GetBlobStorageAsync();
            var src = new StateRepositoryCache(new DirectoryInfo("statecache"), bs, request.Passphrase);
            var sr  = await GetStateRepositoryAsync(src);

            return new HandlerContext
            {
                Request              = request,
                BlobStorage          = bs,
                StateRepositoryCache = src,
                StateRepo            = sr,
                Hasher               = new Sha256Hasher(request.Passphrase),
                FileSystem           = GetFileSystem(),
                Version              = version
            };

            async Task<BlobStorage> GetBlobStorageAsync()
            {
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 0));

                var bs = new BlobStorage(request.AccountName, request.AccountKey, request.ContainerName);
                var created = await bs.CreateContainerIfNotExistsAsync();

                request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 100, created ? "Created" : "Already existed"));

                return bs;
            }

            async Task<StateRepository> GetStateRepositoryAsync(StateRepositoryCache src)
            {
                try
                {
                    request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 0));

                    var lastVersion = await bs.GetStates().LastOrDefaultAsync();
                    var newVersion  = GetVersionFileName(version);

                    if (string.IsNullOrEmpty(lastVersion))
                    {
                        // New repository
                        return new StateRepository(newVersion);
                    }
                    else
                    {
                        // Existing repository, start from last version
                        var local = await src.GetLocalCacheAsync(lastVersion);
                        var kak   = local.CopyTo(Path.Combine(local.DirectoryName, newVersion));

                        return new StateRepository(newVersion);
                    }
                }
                finally
                {
                    request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 100, "Done"));
                }

                string GetVersionFileName(string version) => $"{version}.db";
            }

            FilePairFileSystem GetFileSystem()
            {
                var pfs = new PhysicalFileSystem();
                var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
                var sfs = new SubFileSystem(pfs, root, true);
                return new FilePairFileSystem(sfs, true);
            }
        }

        public required ArchiveCommand       Request              { get; init; }
        public required BlobStorage          BlobStorage          { get; init; }
        public required StateRepository      StateRepo            { get; init; }
        public required StateRepositoryCache StateRepositoryCache { get; init; }
        public required Sha256Hasher         Hasher               { get; init; }
        public required FilePairFileSystem   FileSystem           { get; init; }
        public          string               Version              { get; set; }
        
    }
}
