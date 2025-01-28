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
            await using var blobStream = await handlerContext.BlobStorage.OpenWriteAsync(
                containerName: handlerContext.Request.ContainerName,
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
            var actualTier = await handlerContext.BlobStorage.SetStorageTierPerPolicy(handlerContext.Request.ContainerName, hash, blobStream.Position, handlerContext.Request.Tier);

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

        await foreach (var filePairWithHash in hashedSmallFilesChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                if (tarWriter is null)
                {
                    ms = new MemoryStream();
                    gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true); // Set leaveOpen: true to prevent disposing the MemoryStream when GZipStream is disposed
                    tarWriter = new TarWriter(gzip); // TODO quid usings?
                    originalSize = 0;

                    await gzip.FlushAsync();
                    previousPosition = ms.Position;
                }

                var (filePair, binaryHash) = filePairWithHash;

                // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
                var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, binaryHash);

                handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, $"Queued in TAR..."));

                // 3. Upload the Binary, if needed
                if (needsToBeUploaded)
                {

                    //await using var ss = filePair.BinaryFile.OpenRead();
                    var fn = handlerContext.FileSystem.ConvertPathToInternal(filePair.Path);
                    await tarWriter.WriteEntryAsync(fn, binaryHash.ToString(), cancellationToken);

                    await gzip.FlushAsync(); // flush gzip so we get an accurate position in the memorystream
                    await ms.FlushAsync();

                    originalSize += filePair.BinaryFile.Length;
                    var archivedSize = ms.Position - previousPosition;



                    tarredFilePairs.Add((filePair, binaryHash, archivedSize));

                    previousPosition = ms.Position;

                }
                // the else {} branch is not necessary here since we are sure that the file will be uploaded in this run
                //else { await uploadTask; }



                if ((ms.Position > 1024 * 1024 ||
                     (ms.Position <= 1024 * 1024 && hashedSmallFilesChannel.Reader.Completion.IsCompleted)) && tarredFilePairs.Any())
                {
                    tarWriter.Dispose();
                    gzip.Dispose(); // dispose the gzipstream so it writes the gzip closing block to the memorystream

                    ms.Seek(0, SeekOrigin.Begin);

                    var tarHash = await handlerContext.Hasher.GetHashAsync(ms);

                    File.WriteAllBytes($@"C:\Users\RFC430\Downloads\New folder\{tarHash}.tar.gzip", ms.ToArray());

                    ms.Seek(0, SeekOrigin.Begin);

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

                    // Flush all buffers
                    await cryptoStream.FlushAsync(cancellationToken);
                    await blobStream.FlushAsync(cancellationToken);

                    // Update tier
                    var actualTier = await handlerContext.BlobStorage.SetStorageTierPerPolicy(handlerContext.Request.ContainerName, tarHash, blobStream.Position, handlerContext.Request.Tier);

                    // Add BinaryProperties
                    var bps = tarredFilePairs.Select(x => new BinaryPropertiesDto
                    {
                        Hash = x.Hash,
                        ParentHash = tarHash,
                        OriginalSize = x.FilePair.BinaryFile.Length,
                        ArchivedSize = x.ArchivedSize,
                        StorageTier = actualTier
                    }).ToArray();
                    handlerContext.StateRepo.AddBinaryProperties(bps);

                    handlerContext.StateRepo.AddBinaryProperties(new BinaryPropertiesDto
                    {
                        Hash = tarHash,
                        OriginalSize = originalSize,
                        ArchivedSize = ms.Position,
                        StorageTier = actualTier
                    });

                    // Mark as upladed
                    foreach (var (_, binaryHash2, _) in tarredFilePairs)
                        MarkAsUploaded(binaryHash2);

                    // 4.Write the Pointers
                    var pfes = new List<PointerFileEntryDto>();
                    foreach (var (filePair22, binaryHash22, _) in tarredFilePairs)
                    {
                        var pf = filePair22.GetOrCreatePointerFile(binaryHash22);
                        pfes.Add(new PointerFileEntryDto
                        {
                            Hash = binaryHash22,
                            RelativeName = pf.Path.FullName,
                            CreationTimeUtc = pf.CreationTime,
                            LastWriteTimeUtc = pf.LastWriteTime
                        });
                    }

                    // 5. Write the PointerFileEntry
                    handlerContext.StateRepo.UpsertPointerFileEntries(pfes.ToArray());

                    //handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 100, "Completed"));

                    foreach (var (tarredFilePair, _, _) in tarredFilePairs)
                        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(tarredFilePair.FullName, 100, $"TAR DOne"));


                    ms.Dispose();
                    tarWriter = null;
                    tarredFilePairs.Clear();
                }
            }
            catch (Exception e)
            {
            }
        }
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