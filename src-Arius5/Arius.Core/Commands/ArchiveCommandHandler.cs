using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Humanizer;
using MediatR;
using SharpCompress.Common;
using SharpCompress.Writers;
using System.Threading.Channels;
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

    public int SmallFileBoundary { get; init; } = (int)1.5 * 1024 * 1024; // 1.5 MB

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}

public record ProgressUpdate;
public record TaskProgressUpdate(string TaskName, double Percentage, string? StatusMessage = null) : ProgressUpdate;
public record FileProgressUpdate(string FileName, double Percentage, string? StatusMessage = null) : ProgressUpdate;

internal class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{
    private readonly Dictionary<Hash, TaskCompletionSource> uploadingHashes = new();

    private readonly Channel<FilePair>         indexedFilesChannel     = GetBoundedChannel<FilePair>(capacity: 100,        singleWriter: true, singleReader: false);
    private readonly Channel<FilePairWithHash> hashedLargeFilesChannel = GetBoundedChannel<FilePairWithHash>(capacity: 20, singleWriter: false, singleReader: false);
    private readonly Channel<FilePairWithHash> hashedSmallFilesChannel = GetBoundedChannel<FilePairWithHash>(capacity: 20, singleWriter: false, singleReader: true);

    private record FilePairWithHash(FilePair FilePair, Hash Hash);

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await HandlerContext.CreateAsync(request);

        
        var indexTask   = CreateIndexTask(handlerContext, cancellationToken);
        var hashTask    = CreateHashTask(handlerContext, cancellationToken);
        var uploadLargeFilesTask = CreateUploadLargeFilesTask(handlerContext, cancellationToken);
        var uploadSmallFilesTask = CreateUploadSmallFilesTask(handlerContext, cancellationToken);

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
                    //await UploadLargeFileAsync(handlerContext, filePairWithHash, cancellationToken: innerCancellationToken);
                }
                catch (Exception e)
                {
                }
            });

    private Task CreateUploadSmallFilesTask(HandlerContext handlerContext, CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            try
            {
                MemoryStream ms        = null;
                IWriter      tarWriter = null;

                await foreach (var filePairWithHash in hashedSmallFilesChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        if (tarWriter is null)
                        {
                            ms        = new MemoryStream();
                            tarWriter = WriterFactory.Open(ms, ArchiveType.Tar, new WriterOptions(CompressionType.None)); // TODO quid usings?
                        }

                        var (filePair, h) = filePairWithHash;

                        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
                        var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, h);

                        // 3. Upload the Binary, if needed
                        if (needsToBeUploaded)
                        {
                            await using var ss = filePair.BinaryFile.OpenRead();
                            tarWriter.Write(h.ToString(), ss);
                        }
                        else
                        {
                            await uploadTask;
                        }
                        
                        if (ms.Position > 4000)
                        {
                            File.WriteAllBytes(@"C:\Users\RFC430\Downloads\New folder\test.tar", ms.ToArray());

                            ms.Seek(0, SeekOrigin.Begin);

                            var hh = await handlerContext.Hasher.GetHashAsync(ms);

                            ms.Seek(0, SeekOrigin.Begin);

                            var (actualTier, archivedSize) = await handlerContext.BlobStorage.UploadAsync(
                                source: ms,
                                containerName: handlerContext.Request.ContainerName,
                                h: hh,
                                passphrase: handlerContext.Request.Passphrase,
                                targetTier: handlerContext.Request.Tier,
                                metadata: null,
                                progress: null,
                                cancellationToken: cancellationToken);

                            tarWriter.Dispose();
                            ms.Dispose();
                            tarWriter = null;
                        }
                    }
                    catch (Exception e)
                    {
                    }
                }

                /*
                //var ms = new MemoryStream();
                //var archive = new ZipArchive(ms, ZipArchiveMode.Create); // TODO quid usings?

                //await foreach (var filePairWithHash in hashedSmallFilesChannel.Reader.ReadAllAsync(cancellationToken))
                //{
                //    try
                //    {
                //        var (filePair, h) = filePairWithHash;

                //        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
                //        var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, h);

                //        // 3. Upload the Binary, if needed
                //        if (needsToBeUploaded)
                //        {
                //            var fn = handlerContext.FileSystem.ConvertPathToInternal(filePair.Path);
                //            var entry = archive.CreateEntryFromFile(fn, h.ToString(), CompressionLevel.Optimal);
                //        }
                //        else
                //        {
                //            await uploadTask;
                //        }

                //        if (ms.Position > 1024)
                //        {

                //        }
                //    }
                //    catch (Exception e)
                //    {
                //    }
                //}
                */
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        }, cancellationToken);
    }

    private async Task UploadLargeFileAsync(HandlerContext handlerContext, FilePairWithHash filePairWithHash, CancellationToken cancellationToken = default)
    {
        var (filePair, h) = filePairWithHash;

        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
        var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, h);

        // 3. Upload the Binary, if needed
        if (needsToBeUploaded)
        {
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, $"Uploading {filePair.ExistingBinaryFile?.Length.Bytes().Humanize()}..."));

            await using var ss = filePair.BinaryFile.OpenRead();
            
            // Upload
            var (actualTier, archivedSize) = await handlerContext.BlobStorage.UploadAsync(
                source: ss, 
                containerName: handlerContext.Request.ContainerName, 
                h: h, 
                passphrase: handlerContext.Request.Passphrase, 
                targetTier: handlerContext.Request.Tier, 
                metadata: null, 
                progress: null, 
                cancellationToken: cancellationToken);
            
            // Add to db
            handlerContext.StateRepo.AddBinaryProperty(new BinaryPropertiesDto
            {
                Hash         = h,
                OriginalSize = ss.Length,
                ArchivedSize = archivedSize,
                StorageTier  = actualTier
            });

            // remove from temp
            MarkAsUploaded(h);
        }
        else
        {
            await uploadTask;
        }

        // 4.Write the Pointer
        var pf = filePair.GetOrCreatePointerFile(h);

        // 5. Write the PointerFileEntry
        handlerContext.StateRepo.UpsertPointerFileEntry(new PointerFileEntryDto
        {
            Hash             = h,
            RelativeName     = pf.Path.FullName,
            CreationTimeUtc  = pf.CreationTime,
            LastWriteTimeUtc = pf.LastWriteTime
        });

        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 100, "Completed"));
    }

    //private async Task UploadSmallFileAsync(HandlerContext handlerContext, FilePairWithHash filePairWithHash, CancellationToken cancellationToken = default)
    //{

    //}

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