using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Humanizer;
using MediatR;
using System.Formats.Tar;
using System.IO.Compression;
using System.Threading.Channels;
using WouterVanRanst.Utils.Extensions;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Commands;

public record ArchiveCommand : IRequest
{
    public required string        AccountName   { get; init; }
    public required string        AccountKey    { get; init; }
    public required string        ContainerName { get; init; }
    public required string        Passphrase    { get; init; }
    public required bool          RemoveLocal   { get; init; }
    public required StorageTier   Tier          { get; init; }
    public required DirectoryInfo LocalRoot     { get; init; }

    public int Parallelism { get; init; } = 1;

    public int SmallFileBoundary { get; init; } = 2 * 1024 * 1024; // 2 MB

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}

public record ProgressUpdate;
public record TaskProgressUpdate(string TaskName, double Percentage, string? StatusMessage = null) : ProgressUpdate;
public record FileProgressUpdate(string FileName, double Percentage, string? StatusMessage = null) : ProgressUpdate;

internal class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{
    private readonly Dictionary<Hash, TaskCompletionSource> uploadingHashes = new();

    private readonly Channel<FilePair>         indexedFilesChannel     = GetBoundedChannel<FilePair>(capacity: 20,        singleWriter: true, singleReader: false);
    private readonly Channel<FilePairWithHash> hashedLargeFilesChannel = GetBoundedChannel<FilePairWithHash>(capacity: 10, singleWriter: false, singleReader: false);
    private readonly Channel<FilePairWithHash> hashedSmallFilesChannel = Channel.CreateUnbounded<FilePairWithHash>(new UnboundedChannelOptions() { AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = true }); // unbounded since there can be a deadlock 
    //private readonly Channel<FilePairWithHash> hashedSmallFilesChannel = GetBoundedChannel<FilePairWithHash>(capacity: 20, singleWriter: false, singleReader: true);

    private record FilePairWithHash(FilePair FilePair, Hash Hash);

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operationToken = linkedCts.Token;

        try
        {
            var handlerContext = await HandlerContext.CreateAsync(request, operationToken);

            var indexTask = CreateIndexTask(handlerContext, operationToken);
            var hashTask = CreateHashTask(handlerContext, operationToken);
            var uploadLargeFilesTask = CreateUploadLargeFilesTask(handlerContext, operationToken);
            var uploadSmallFilesTask = CreateUploadSmallFilesTarArchiveTask(handlerContext, operationToken);

            // Await all core processing tasks. If one fails, Task.WhenAll will throw.
            await Task.WhenAll(indexTask, hashTask, uploadLargeFilesTask, uploadSmallFilesTask);

            // This part only runs if all tasks above succeeded
            handlerContext.StateRepo.DeletePointerFileEntries(pfe => !handlerContext.FileSystem.FileExists(pfe.RelativeName));
        }
        catch (Exception ex)
        {
            // If the exception is not already a cancellation, signal cancellation to other tasks.
            if (!(ex is OperationCanceledException))
            {
                // Suppress exceptions from Cancel itself (e.g. if already disposed, though unlikely here)
                try { linkedCts.Cancel(); } catch { /* ignored */ }
            }
            throw; // Propagate the original exception
        }
    }

    private Task CreateIndexTask(HandlerContext handlerContext, CancellationToken cancellationToken) =>
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
            catch (OperationCanceledException e)
            {
                indexedFilesChannel.Writer.TryComplete(e);
                throw;
            }
            catch (Exception e)
            {
                indexedFilesChannel.Writer.TryComplete(e);
                throw;
            }
        }, cancellationToken);

    private Task CreateHashTask(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(indexedFilesChannel.Reader.ReadAllAsync(cancellationToken),
                    new ParallelOptions { MaxDegreeOfParallelism = handlerContext.Request.Parallelism, CancellationToken = cancellationToken },
                    async (filePair, innerCancellationToken) =>
                    {
                        try
                        {
                            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 10, $"Hashing {filePair.ExistingBinaryFile?.Length.Bytes().Humanize()} ..."));

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
                        catch (IOException e)
                        {
                            // Optionally log: Console.Error.WriteLine($"IOException during hashing {filePair.FullName}: {e.Message}");
                            throw; // Rethrow to make it a fatal error for the hash task
                        }
                        catch (Exception e)
                        {
                            // Optionally log: Console.Error.WriteLine($"Exception during hashing {filePair.FullName}: {e.Message}");
                            throw; // Rethrow
                        }
                    });

                // If Parallel.ForEachAsync completes successfully
                hashedSmallFilesChannel.Writer.TryComplete();
                hashedLargeFilesChannel.Writer.TryComplete();
            }
            catch (OperationCanceledException e) // Catches cancellation from Parallel.ForEachAsync or ReadAllAsync
            {
                hashedSmallFilesChannel.Writer.TryComplete(e);
                hashedLargeFilesChannel.Writer.TryComplete(e);
                throw;
            }
            catch (Exception e) // Catches AggregateException from Parallel.ForEachAsync or other exceptions
            {
                hashedSmallFilesChannel.Writer.TryComplete(e);
                hashedLargeFilesChannel.Writer.TryComplete(e);
                throw;
            }
        }, cancellationToken);
    }

    private Task CreateUploadLargeFilesTask(HandlerContext handlerContext, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(hashedLargeFilesChannel.Reader.ReadAllAsync(cancellationToken),
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
                            // Optionally log: Console.Error.WriteLine($"Error uploading large file {filePairWithHash.FilePair.FullName}: {e.Message}");
                            throw; // Rethrow to make it a fatal error for the upload task
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) // Catches AggregateException or other exceptions
            {
                throw;
            }
        }, cancellationToken);

    private Task CreateUploadSmallFilesTarArchiveTask(HandlerContext handlerContext, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            MemoryStream? ms = null;
            TarWriter? tarWriter = null;
            GZipStream? gzip = null;
            var tarredFilePairs = new List<(FilePair FilePair, Hash Hash, long ArchivedSize)>();
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
                        // try-catch for stream initialization is good practice but DisposeAsync in finally covers it too
                        gzip             = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true);
                        tarWriter        = new TarWriter(gzip, leaveOpen: false); // TarWriter will dispose GZipStream, GZipStream will leave MemoryStream open
                        originalSize     = 0;
                        previousPosition = ms.Position; // Should be 0 after GZipStream header
                    }

                    var (filePair, binaryHash)          = filePairWithHash;
                    var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, binaryHash);
                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, $"Queued in TAR..."));

                    if (needsToBeUploaded)
                    {
                        var fn = handlerContext.FileSystem.ConvertPathToInternal(filePair.Path);
                        await tarWriter.WriteEntryAsync(fileName: fn, entryName: binaryHash.ToString(), cancellationToken: cancellationToken);

                        await gzip.FlushAsync(cancellationToken);
                        await ms.FlushAsync(cancellationToken);

                        originalSize += filePair.BinaryFile.Length;
                        var archivedSize = ms.Position - previousPosition;
                        tarredFilePairs.Add((filePair, binaryHash, archivedSize));
                        previousPosition = ms.Position;
                    }
                    else
                    {
                        await uploadTask; // If not uploading now, ensure any prior upload task for this hash is awaited
                    }

                    bool shouldUploadTar = tarredFilePairs.Any() &&
                                           (ms.Position > 1024 * 1024 || // Configurable TAR size limit
                                            tarredFilePairs.Count >= 10 || // Configurable TAR file count limit
                                            (hashedSmallFilesChannel.Reader.Completion.IsCompleted && ms.Position > 0));

                    if (shouldUploadTar)
                    {
                        await tarWriter.DisposeAsync(); // Flushes and disposes TarWriter and underlying GZipStream
                        tarWriter = null;
                        gzip      = null; // Mark as disposed

                        ms.Seek(0, SeekOrigin.Begin);
                        // Assuming GetHashAsync(Stream, CancellationToken) exists
                        var tarHash = await handlerContext.Hasher.GetHashAsync(ms);
                        ms.Seek(0, SeekOrigin.Begin);

                        File.WriteAllBytes($@"C:\Users\RFC430\Downloads\New folder\{tarHash}.tar.gzip", ms.ToArray());

                        foreach (var (tarredFilePair, _, _) in tarredFilePairs)
                            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(tarredFilePair.FullName, 70, $"Uploading TAR..."));

                        await using var blobStream = await handlerContext.BlobStorage.OpenWriteAsync(
                            containerName: handlerContext.Request.ContainerName,
                            h: tarHash,
                            contentType: TarChunkContentType,
                            metadata: null,
                            progress: null,
                            cancellationToken: cancellationToken);
                        await using var cryptoStream = await blobStream.GetCryptoStreamAsync(handlerContext.Request.Passphrase, cancellationToken);
                        await ms.CopyToAsync(cryptoStream, bufferSize: 1024 * 1024 * 2, cancellationToken);

                        await cryptoStream.FlushAsync(cancellationToken);
                        await blobStream.FlushAsync(cancellationToken);

                        var actualTier = await handlerContext.BlobStorage.SetStorageTierPerPolicy(handlerContext.Request.ContainerName, tarHash, blobStream.Position, handlerContext.Request.Tier);

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

                        foreach (var (_, binaryHash2, _) in tarredFilePairs)
                            MarkAsUploaded(binaryHash2);

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

                        handlerContext.StateRepo.UpsertPointerFileEntries(pfes.ToArray());

                        foreach (var (tarredFilePair, _, _) in tarredFilePairs)
                            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(tarredFilePair.FullName, 100, $"TAR Done"));

                        await ms.DisposeAsync();
                        ms = null; // Reset for the next batch
                        tarredFilePairs.Clear();
                        originalSize     = 0;
                        previousPosition = 0;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // Console.WriteLine(e); // Original logging
                throw; // Rethrow original exception
            }
            finally
            {
                // Ensure streams are disposed if an exception occurs mid-batch
                if (tarWriter is not null) await tarWriter.DisposeAsync(); // Disposes underlying GZipStream too if not left open
                else if (gzip is not null) await gzip.DisposeAsync(); // If tarWriter was null but gzip was initialized
                if (ms is not null) await ms.DisposeAsync();
            }
        }, cancellationToken);

    private const string ChunkContentType = "application/aes256cbc+gzip";
    private const string TarChunkContentType = "application/aes256cbc+tar+gzip";


    private async Task UploadLargeFileAsync(HandlerContext handlerContext, FilePairWithHash filePairWithHash, CancellationToken cancellationToken = default)
    {
        var (filePair, hash) = filePairWithHash;
        var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, hash);

        if (needsToBeUploaded)
        {
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, $"Uploading {filePair.ExistingBinaryFile?.Length.Bytes().Humanize()}..."));

            await using var blobStream = await handlerContext.BlobStorage.OpenWriteAsync(
                containerName: handlerContext.Request.ContainerName,
                h: hash,
                contentType: ChunkContentType,
                metadata: null,
                progress: null,
                cancellationToken: cancellationToken);
            await using var cryptoStream = await blobStream.GetCryptoStreamAsync(handlerContext.Request.Passphrase, cancellationToken);
            await using var gzs = new GZipStream(cryptoStream, CompressionLevel.Optimal);
            await using var ss = filePair.BinaryFile.OpenRead();

            await ss.CopyToAsync(gzs, bufferSize: 81920, cancellationToken);
            await gzs.FlushAsync(cancellationToken); // Ensure GZipStream is flushed before CryptoStream
            // CryptoStream will be flushed on DisposeAsync by await using
            // BlobStream will be flushed on DisposeAsync by await using

            var actualTier = await handlerContext.BlobStorage.SetStorageTierPerPolicy(handlerContext.Request.ContainerName, hash, blobStream.Position, handlerContext.Request.Tier);

            handlerContext.StateRepo.AddBinaryProperties(new BinaryPropertiesDto
            {
                Hash = hash,
                OriginalSize = ss.Position, // Original size from source stream
                ArchivedSize = blobStream.Position, // Archived size from blob stream
                StorageTier = actualTier
            });

            MarkAsUploaded(hash);
        }
        else
        {
            await uploadTask; // Await the existing upload task
        }

        var pf = filePair.GetOrCreatePointerFile(hash);
        handlerContext.StateRepo.UpsertPointerFileEntries(new PointerFileEntryDto
        {
            Hash = hash,
            RelativeName = pf.Path.FullName,
            CreationTimeUtc = pf.CreationTime,
            LastWriteTimeUtc = pf.LastWriteTime
        });

        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 100, "Completed"));
    }

    // -- UPLOAD STATUS HELPERS (no changes needed for error propagation logic)
    private (bool needsToBeUploaded, Task uploadTask) GetUploadStatus(HandlerContext handlerContext, Hash h)
    {
        var bp = handlerContext.StateRepo.GetBinaryProperty(h);
        lock (uploadingHashes)
        {
            if (bp is null)
            {
                if (uploadingHashes.TryGetValue(h, out var tcs))
                {
                    return (false, tcs.Task);
                }
                else
                {
                    tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    uploadingHashes.Add(h, tcs);
                    return (true, tcs.Task);
                }
            }
            else
            {
                return (false, Task.CompletedTask);
            }
        }
    }

    private void MarkAsUploaded(Hash h)
    {
        lock (uploadingHashes)
        {
            if (uploadingHashes.Remove(h, out var tcs))
            {
                tcs.SetResult();
            }
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

    private class HandlerContext
    {
        // Pass CancellationToken to CreateAsync and its sub-methods
        public static async Task<HandlerContext> CreateAsync(ArchiveCommand request, CancellationToken cancellationToken)
        {
            return new HandlerContext
            {
                Request = request,
                BlobStorage = await GetBlobStorageAsync(request, cancellationToken),
                StateRepo = await GetStateRepositoryAsync(request, cancellationToken),
                Hasher = new Sha256Hasher(request.Passphrase),
                FileSystem = GetFileSystem(request)
            };
        }

        // Made static and passed request/cancellationToken explicitly
        private static async Task<BlobStorage> GetBlobStorageAsync(ArchiveCommand request, CancellationToken cancellationToken)
        {
            request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 0));
            var bs = new BlobStorage(request.AccountName, request.AccountKey);
            // Assuming CreateContainerIfNotExistsAsync accepts a CancellationToken
            var created = await bs.CreateContainerIfNotExistsAsync(request.ContainerName);
            request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 100, created ? "Created" : "Already existed"));
            return bs;
        }

        private static async Task<StateRepository> GetStateRepositoryAsync(ArchiveCommand request, CancellationToken cancellationToken)
        {
            request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 0));
            cancellationToken.ThrowIfCancellationRequested();
            // Assuming StateRepository constructor is synchronous and quick.
            // If it were async: var repo = await StateRepository.CreateAsync(cancellationToken);
            var repo = new StateRepository();
            request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 100, "Done"));
            return repo; // Return repo, await is not needed for synchronous constructor
        }

        private static FilePairFileSystem GetFileSystem(ArchiveCommand request)
        {
            var pfs = new PhysicalFileSystem();
            var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
            var sfs = new SubFileSystem(pfs, root, true);
            return new FilePairFileSystem(sfs, true);
        }


        public required ArchiveCommand Request { get; init; }
        public required BlobStorage BlobStorage { get; init; }
        public required StateRepository StateRepo { get; init; }
        public required Sha256Hasher Hasher { get; init; }
        public required FilePairFileSystem FileSystem { get; init; }
    }
}