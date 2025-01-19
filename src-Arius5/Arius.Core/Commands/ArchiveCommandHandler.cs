using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Humanizer;
using MediatR;
using System.Net;
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

    public int Parallelism { get; init; } = 5;

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}

public record ProgressUpdate;
public record TaskProgressUpdate(string TaskName, double Percentage, string? StatusMessage = null) : ProgressUpdate;
public record FileProgressUpdate(string FileName, double Percentage, string? StatusMessage = null) : ProgressUpdate;

internal class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{
    private readonly Dictionary<Hash, TaskCompletionSource> uploadingHashes = new();

    private record FilePairWithHash(FilePair FilePair, Hash Hash);

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await HandlerContext.CreateAsync(request);

        var indexedFilesChannel = GetBoundedChannel<FilePair>(100, true);
        var hashedFilesChannel  = GetBoundedChannel<FilePairWithHash>(20, true);

        var indexTask   = CreateIndexTask(handlerContext, indexedFilesChannel, cancellationToken);
        var hashTask    = CreateHashTask(handlerContext, indexedFilesChannel, hashedFilesChannel, cancellationToken);
        var processTask = ProcessTask(handlerContext, hashedFilesChannel, cancellationToken);

        await processTask;
        //await indexTask;

        // 6. Remove PointerFileEntries that do not exist on disk
        handlerContext.StateRepo.DeletePointerFileEntries(pfe => !handlerContext.FileSystem.FileExists(pfe.RelativeName));
    }

    private Task CreateIndexTask(HandlerContext handlerContext, Channel<FilePair> indexedFilesChannel, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate($"Indexing '{handlerContext.Request.LocalRoot}'...", 0));

            int fileCount = 0;
            foreach (var fp in handlerContext.FileSystem.EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories))
            {
                Interlocked.Increment(ref fileCount);
                await indexedFilesChannel.Writer.WriteAsync(FilePair.FromFileEntry(fp), cancellationToken);
            }

            indexedFilesChannel.Writer.Complete();

            handlerContext.Request.ProgressReporter?.Report(new TaskProgressUpdate($"Indexing '{handlerContext.Request.LocalRoot}'...", 100, $"Found {fileCount} files"));
        }, cancellationToken);

    private Task CreateHashTask(HandlerContext handlerContext, Channel<FilePair> indexedFilesChannel, Channel<FilePairWithHash> hashedFilesChannel, CancellationToken cancellationToken)
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

                    await hashedFilesChannel.Writer.WriteAsync(new(filePair, h), cancellationToken: innerCancellationToken);

                    handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 50, $"Hashing done, waiting in upload queue..."));
                }
                catch (Exception e)
                {
                }

            });

        t.ContinueWith(_ => hashedFilesChannel.Writer.Complete());

        return t;
    }

    private Task ProcessTask(HandlerContext handlerContext, Channel<FilePairWithHash> hashedFilesChannel, CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(hashedFilesChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions { MaxDegreeOfParallelism = handlerContext.Request.Parallelism, CancellationToken = cancellationToken },
            async (filePairWithHash, innerCancellationToken) =>
            {
                try
                {
                    await UploadFileAsync(handlerContext, filePairWithHash, cancellationToken: innerCancellationToken);
                }
                catch (Exception e)
                {
                }
            });

    private async Task UploadFileAsync(HandlerContext handlerContext, FilePairWithHash filePairWithHash, CancellationToken cancellationToken = default)
    {
        var (filePair, h) = filePairWithHash;

        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
        var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, h);

        // 3. Upload the Binary, if needed
        if (needsToBeUploaded)
        {
            handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 60, "Uploading..."));

            var bbc = handlerContext.ContainerClient.GetBlockBlobClient($"chunks/{h.ToLongString()}");

            var ss = filePair.BinaryFile.OpenRead();
            var ts = await OpenWriteAsync(bbc, throwOnExists: false, cancellationToken: cancellationToken);

            // Upload
            await ss.CopyToCompressedEncryptedAsync(ts, handlerContext.Request.Passphrase);

            // Set access tier
            var actualTier = GetPolicyAccessTier(handlerContext, ts.Position);
            await bbc.SetAccessTierAsync(actualTier, cancellationToken: cancellationToken);

            // Add to db
            handlerContext.StateRepo.AddBinaryProperty(new BinaryPropertiesDto
            {
                Hash = h.Value,
                OriginalSize = ss.Length,
                ArchivedSize = ts.Position,
                StorageTier = actualTier.ToStorageTier()
            });

            // remove from temp
            MarkAsUploaded(h);
        }
        else
        {
            await uploadTask;
        }

        // 4. Write the Pointer
        //var pf = filePair.GetOrCreatePointerFile(h);

        //// 5. Write the PointerFileEntry
        //handlerContext.StateRepo.UpsertPointerFileEntry(new PointerFileEntryDto
        //{
        //    Hash = h.Value,
        //    RelativeName = pf.Path.FullName,
        //    CreationTimeUtc = pf.CreationTime,
        //    LastWriteTimeUtc = pf.LastWriteTime
        //});
        
        //await Task.Delay(2000);


        handlerContext.Request.ProgressReporter?.Report(new FileProgressUpdate(filePair.FullName, 100, "Completed"));

    }

    private static AccessTier GetPolicyAccessTier(HandlerContext handlerContext, long length)
    {
        const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

        if (handlerContext.Request.Tier == StorageTier.Archive && length <= oneMegaByte)
            return AccessTier.Cold; //Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive

        return handlerContext.Request.Tier.ToAccessTier();
    }


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

    private static async Task<Stream> OpenWriteAsync(BlockBlobClient bbc, /*string contentType = ICryptoService.ContentType, */IDictionary<string, string>? metadata = default, bool throwOnExists = true, CancellationToken cancellationToken = default)
    {
        var bbowo = new BlockBlobOpenWriteOptions();

        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        if (throwOnExists)
            bbowo.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }; // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
        if (metadata is not null)
            bbowo.Metadata = metadata;
        //bbowo.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };

        return await bbc.OpenWriteAsync(overwrite: true, options: bbowo, cancellationToken: cancellationToken);
    }



    private static Channel<T> GetBoundedChannel<T>(int capacity, bool singleWriter)
        => Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
            SingleWriter = singleWriter,
            SingleReader = false
        });

    //private static ParallelOptions GetParallelOptions(int maxDegreeOfParallelism, CancellationToken cancellationToken = default)
    //    => new() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };

    

    //private PointerFile CreatePointerFile(BinaryFile bf, Hash h)
    //{
    //    var pf = bf.GetPointerFile();

    //    pf.Write(h, bf.CreationTime, bf.LastWriteTime);

    //    return pf;
    //}



    private class HandlerContext
    {
        public static async Task<HandlerContext> CreateAsync(ArchiveCommand request)
        {
            return new HandlerContext
            {
                Request = request,
                ContainerClient = await GetContainerClientAsync(),
                StateRepo = await GetStateRepositoryAsync(),
                Hasher = new SHA256Hasher(request.Passphrase),
                FileSystem = GetFileSystem()
            };


            async Task<BlobContainerClient> GetContainerClientAsync()
            {
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 0));

                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={request.AccountName};AccountKey={request.AccountKey};EndpointSuffix=core.windows.net";
                var bbc              = new BlobContainerClient(connectionString, request.ContainerName, new BlobClientOptions());

                var r = await bbc.CreateIfNotExistsAsync(PublicAccessType.None);

                if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
                    request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 100, "Created"));
                else
                    request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 100, "Already existed"));

                return bbc;
            }

            async Task<StateRepository> GetStateRepositoryAsync()
            {
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 0));

                var r = new StateRepository();

                request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 100, "Done"));

                return r;
            }

            FilePairFileSystem GetFileSystem()
            {
                var pfs = new PhysicalFileSystem();
                var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
                var sfs = new SubFileSystem(pfs, root, true);
                return new FilePairFileSystem(sfs, true);
            }
        }

        public required ArchiveCommand      Request         { get; init; }
        public required BlobContainerClient ContainerClient { get; init; }
        public required StateRepository     StateRepo       { get; init; }
        public required SHA256Hasher        Hasher          { get; init; }
        public required FilePairFileSystem  FileSystem      { get; init; }
    }
}