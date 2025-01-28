using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Humanizer;
using MediatR;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.CompilerServices;
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

    public int SmallFileBoundary { get; init; } = (int)2 * 1024 * 1024; // 1.5 MB

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
    // private readonly Channel<FilePairWithHash> hashedSmallFilesChannel = Channel.CreateUnbounded<FilePairWithHash>(new UnboundedChannelOptions() { AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = true }); // unbounded since there can be a deadlock 
    private readonly Channel<FilePairWithHash> hashedSmallFilesChannel = GetBoundedChannel<FilePairWithHash>(capacity: 10, singleWriter: false, singleReader: true);

    private record FilePairWithHash(FilePair FilePair, Hash Hash);

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await HandlerContext.CreateAsync(request);

        
        var indexTask   = CreateIndexTask(handlerContext, cancellationToken);
        var hashTask    = CreateHashTask(handlerContext, cancellationToken);
        var uploadLargeFilesTask = CreateUploadLargeFilesTask(handlerContext, cancellationToken);
        var uploadSmallFilesTask = CreateUploadSmallFilesTarArchiveTask(handlerContext, cancellationToken);

        await Task.WhenAll(uploadLargeFilesTask, uploadSmallFilesTask);
        //await indexTask;

        // 6. Remove PointerFileEntries that do not exist on disk
        handlerContext.StateRepo.DeletePointerFileEntries(pfe => !handlerContext.FileSystem.FileExists(pfe.RelativeName));
    }

    private Task CreateIndexTask(HandlerContext handlerContext, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate($"Indexing '{handlerContext.Request.LocalRoot}'...", 0));

            int fileCount = 0;
            foreach (var fp in handlerContext.FileSystem.EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories))
            {
                Interlocked.Increment(ref fileCount);
                await indexedFilesChannel.Writer.WriteAsync(FilePair.FromBinaryFileFileEntry(fp), cancellationToken);
            }

            indexedFilesChannel.Writer.Complete();

            handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate($"Indexing '{handlerContext.Request.LocalRoot}'...", 100, $"Found {fileCount} files"));
        }, cancellationToken);

    private Task CreateHashTask(HandlerContext handlerContext, CancellationToken cancellationToken)
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
                catch (IOException e)
                {
                    // TODO when the file cannot be accessed
                }
                catch (Exception e)
                {
                }

            });

        t.ContinueWith(_ =>
        {
            hashedSmallFilesChannel.Writer.Complete();
            hashedLargeFilesChannel.Writer.Complete();
        });

        return t;
    }

    private Task CreateUploadLargeFilesTask(HandlerContext handlerContext, CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(hashedLargeFilesChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions { MaxDegreeOfParallelism = handlerContext.Request.Parallelism, CancellationToken = cancellationToken },
            async (filePairWithHash, innerCancellationToken) =>
            {
                try
                {
                    await UploadLargeFileAsync(handlerContext, filePairWithHash, cancellationToken: innerCancellationToken);
                }
                catch (Exception e)
                {
                }
            });

    private Task CreateUploadSmallFilesTarArchiveTask(HandlerContext handlerContext, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            try
            {
                await UploadSmallFileAsync(handlerContext, cancellationToken);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        }, cancellationToken);

    private const string ChunkContentType = "application/aes256cbc+gzip";
    private const string TarChunkContentType = "application/aes256cbc+tar+gzip";


    private async Task UploadLargeFileAsync(HandlerContext handlerContext, FilePairWithHash filePairWithHash, CancellationToken cancellationToken)
    {
        var (filePair, hash) = filePairWithHash;

        try
        {
            var (needsUpload, uploadTask) = GetUploadStatus(handlerContext, hash);
            if (!needsUpload)
            {
                await uploadTask;
                return;
            }

            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(
                filePair.FullName, 60, $"Uploading {filePair.ExistingBinaryFile?.Length.Bytes().Humanize()}..."));

            // Upload logic
            await using var fileStream = filePair.BinaryFile.OpenRead();
            var result = await UploadBlobAsync(
                handlerContext,
                hash,
                fileStream,
                ChunkContentType,
                filePair.BinaryFile.Length,
                cancellationToken);

            // Create pointer file
            CreatePointerFileEntry(handlerContext, filePair, hash);

            handlerContext.Request.ProgressReporter?.Report(
                new FileProgressUpdate(filePair.FullName, 100, "Completed"));
        }
        catch (Exception ex)
        {
            handlerContext.Request.ProgressReporter?.Report(
                new FileProgressUpdate(filePair.FullName, -1, $"Error: {ex.Message}"));
            MarkAsUploaded(hash, ex);
            throw;
        }
    }

    private async Task UploadSmallFileAsync(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var batch in GetSmallFileBatches(handlerContext, cancellationToken))
            {
                var (tarHash, originalSize, fileEntries) = await ProcessSmallFileBatch(handlerContext, batch, cancellationToken);

                // Upload the tar
                await using var memStream = batch.TarStream;
                memStream.Position = 0; // Reset for reading

                var result = await UploadBlobAsync(
                    handlerContext,
                    tarHash,
                    memStream,
                    TarChunkContentType,
                    originalSize,
                    cancellationToken);

                // Update database entries
                UpdateDatabaseForSmallFiles(handlerContext, tarHash, result, fileEntries);
            }
        }
        catch (Exception ex)
        {
            handlerContext.Request.ProgressReporter?.Report(
                new TaskProgressUpdate("Small file processing", -1, $"Error: {ex.Message}"));
            throw;
        }
    }

    // Common helper methods
    private async Task<BlobUploadResult> UploadBlobAsync(
        HandlerContext context,
        Hash hash,
        Stream contentStream,
        string contentType,
        long originalSize,
        CancellationToken cancellationToken)
    {
        await using var blobStream = await context.BlobStorage.OpenWriteAsync(
            context.Request.ContainerName,
            hash,
            contentType,
            metadata: null,
            progress: null,
            cancellationToken);

        await using var cryptoStream = await blobStream.GetCryptoStreamAsync(context.Request.Passphrase, cancellationToken);

        await contentStream.CopyToAsync(cryptoStream, bufferSize: 81920, cancellationToken);
        await cryptoStream.FlushAsync(cancellationToken);

        var actualTier = await context.BlobStorage.SetStorageTierPerPolicy(
            context.Request.ContainerName,
            hash,
            blobStream.Position,
            context.Request.Tier);

        return new BlobUploadResult(
            Hash: hash,
            OriginalSize: originalSize,
            ArchivedSize: blobStream.Position,
            StorageTier: actualTier);
    }

    private record BlobUploadResult(Hash Hash, long OriginalSize, long ArchivedSize, StorageTier StorageTier);

    private async IAsyncEnumerable<SmallFileBatch> GetSmallFileBatches(
        HandlerContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var batch = new SmallFileBatch();

        await foreach (var filePairWithHash in hashedSmallFilesChannel.Reader.ReadAllAsync(cancellationToken))
        {
            var (filePair, hash) = filePairWithHash;
            var (needsUpload, _) = GetUploadStatus(context, hash);

            if (!needsUpload)
                continue;

            batch.AddFile(filePair, hash);
            context.Request.ProgressReporter?.Report(new FileProgressUpdate(
                filePair.FullName, 70, $"Added to TAR batch ({batch.CurrentSizeBytes.Bytes().Humanize()})"));

            if (batch.CurrentSizeBytes >= 1 * 1024 * 1024)
            {
                yield return await batch.Seal();
                batch = new SmallFileBatch();
            }
        }

        if (batch.Any())
        {
            yield return await batch.Seal();
        }
    }

    private async Task<(Hash TarHash, long OriginalSize, List<FileEntry> FileEntries)> ProcessSmallFileBatch(
        HandlerContext context,
        SmallFileBatch batch,
        CancellationToken cancellationToken)
    {
        await using var tarStream = new MemoryStream();
        await using var gzip = new GZipStream(tarStream, CompressionLevel.Optimal, leaveOpen: true);
        await using var tarWriter = new TarWriter(gzip);

        long originalSize = 0;
        var fileEntries = new List<FileEntry>();

        foreach (var (filePair, hash) in batch.Files)
        {
            var entryName = context.FileSystem.ConvertPathToInternal(filePair.Path);
            await tarWriter.WriteEntryAsync(entryName, hash.ToString(), cancellationToken);
            originalSize += filePair.BinaryFile.Length;
            fileEntries.Add(new FileEntry(filePair, hash));
        }

        await gzip.FlushAsync();
        tarStream.Position = 0;

        return (await context.Hasher.GetHashAsync(tarStream), originalSize, fileEntries);
    }

    private void UpdateDatabaseForSmallFiles(
        HandlerContext context,
        Hash tarHash,
        BlobUploadResult result,
        List<FileEntry> fileEntries)
    {
        // Update parent entry
        context.StateRepo.AddBinaryProperties(new BinaryPropertiesDto
        {
            Hash = tarHash,
            OriginalSize = result.OriginalSize,
            ArchivedSize = result.ArchivedSize,
            StorageTier = result.StorageTier
        });

        // Update child entries
        var childEntries = fileEntries.Select(x => new BinaryPropertiesDto
        {
            Hash = x.Hash,
            ParentHash = tarHash,
            OriginalSize = x.FilePair.BinaryFile.Length,
            ArchivedSize = -1, // Not tracked for individual files in tar
            StorageTier = result.StorageTier
        });

        context.StateRepo.AddBinaryProperties(childEntries.ToArray());

        // Create pointer files
        var pointerEntries = fileEntries.Select(x => CreatePointerFileEntry(context, x.FilePair, x.Hash));
        context.StateRepo.UpsertPointerFileEntries(pointerEntries.ToArray());
    }

    // Helper records
    private record FileEntry(FilePair FilePair, Hash Hash);
    private sealed record SmallFileBatch
    {
        private readonly List<(FilePair FilePair, Hash Hash)>          _files;
        public           IReadOnlyList<(FilePair FilePair, Hash Hash)> Files            { get; }
        public           long                                          CurrentSizeBytes { get; }
        public           MemoryStream                                  TarStream        { get; }

        public SmallFileBatch()
        {
            _files           = new List<(FilePair, Hash)>();
            Files            = _files.AsReadOnly();
            CurrentSizeBytes = 0;
            TarStream        = new MemoryStream();
        }

        private SmallFileBatch(List<(FilePair FilePair, Hash Hash)> files, long currentSizeBytes, MemoryStream tarStream)
        {
            _files           = files;
            Files            = _files.AsReadOnly();
            CurrentSizeBytes = currentSizeBytes;
            TarStream        = tarStream;
        }

        public void AddFile(FilePair filePair, Hash hash)
        {
            _files.Add((filePair, hash));


            var newFiles = new List<(FilePair, Hash)>(_files) { (filePair, hash) 
            };
            return new SmallFileBatch(newFiles, CurrentSizeBytes + filePair.BinaryFile.Length, TarStream);
        }

        public async Task<SmallFileBatch> Seal()
        {
            var sealedStream = new MemoryStream();
            await using (var gzip = new GZipStream(sealedStream, CompressionLevel.Optimal, leaveOpen: true))
            await using (var tarWriter = new TarWriter(gzip))
            {
                foreach (var (filePair, hash) in _files)
                {
                    var             fn         = filePair.FileSystem.ConvertPathToInternal(filePair.Path);
                    await tarWriter.WriteEntryAsync(fn, hash.ToString());
                }
            }

            return new SmallFileBatch(
                new List<(FilePair, Hash)>(_files),
                CurrentSizeBytes,
                sealedStream);
        }

        public bool Any() => _files.Count > 0;
    }

    private PointerFileEntryDto CreatePointerFileEntry(HandlerContext context, FilePair filePair, Hash hash)
    {
        var pf = filePair.GetOrCreatePointerFile(hash);

        return new PointerFileEntryDto
        {
            Hash             = hash,
            RelativeName     = pf.Path.FullName,
            CreationTimeUtc  = pf.CreationTime,
            LastWriteTimeUtc = pf.LastWriteTime
        };
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

    private void MarkAsUploaded(Hash h, Exception exception = null)
    {
        lock (uploadingHashes)
        {
            if (uploadingHashes.Remove(h, out var tcs))
            {
                if (exception != null)
                    tcs.TrySetException(exception);
                else
                    tcs.TrySetResult();
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

    //private static ParallelOptions GetParallelOptions(int maxDegreeOfParallelism, CancellationToken cancellationToken = default)
    //    => new() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };

    private class HandlerContext
    {
        public static async Task<HandlerContext> CreateAsync(ArchiveCommand request)
        {
            return new HandlerContext
            {
                Request     = request,
                BlobStorage = await GetBlobStorageAsync(),
                StateRepo   = await GetStateRepositoryAsync(),
                Hasher      = new Sha256Hasher(request.Passphrase),
                FileSystem  = GetFileSystem()
            };

            async Task<BlobStorage> GetBlobStorageAsync()
            {
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 0));

                var bs = new BlobStorage(request.AccountName, request.AccountKey);
                var created = await bs.CreateContainerIfNotExistsAsync(request.ContainerName);

                request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 100, created ? "Created" : "Already existed"));

                return bs;
            }

            async Task<StateRepository> GetStateRepositoryAsync()
            {
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 0));

                try
                {
                    return new StateRepository();
                }
                catch (Exception e)
                {
                    throw;
                }
                
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 100, "Done"));
            }

            FilePairFileSystem GetFileSystem()
            {
                var pfs = new PhysicalFileSystem();
                var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
                var sfs = new SubFileSystem(pfs, root, true);
                return new FilePairFileSystem(sfs, true);
            }
        }

        public required ArchiveCommand     Request     { get; init; }
        public required BlobStorage        BlobStorage { get; init; }
        public required StateRepository    StateRepo   { get; init; }
        public required Sha256Hasher       Hasher      { get; init; }
        public required FilePairFileSystem FileSystem  { get; init; }
    }
}