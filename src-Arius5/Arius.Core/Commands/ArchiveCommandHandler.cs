using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using MediatR;
using Microsoft.Extensions.Logging;
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
}

internal class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{
    private readonly ILogger<ArchiveCommandHandler>         logger;
    private readonly Dictionary<Hash, TaskCompletionSource> uploadingHashes = new();

    public ArchiveCommandHandler(ILogger<ArchiveCommandHandler> logger)
    {
        this.logger = logger;
    }

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = new HandlerContext(request);

        var c = GetBoundedChannel<FilePair>(100, true);

        var parallelism = 0;

        var pt = Parallel.ForEachAsync(c.Reader.ReadAllAsync(),
            //new ParallelOptions(),
            GetParallelOptions(1),
            async (fp, ct) =>
            {
                Interlocked.Increment(ref parallelism);

                try
                {
                    //if (fp.BinaryFile.Exists && fp.BinaryFile.Length > 1024 * 1024 * 10)
                    //    return;

                    await UploadFileAsync(handlerContext, fp, cancellationToken: ct);
                }
                catch (Exception e)
                {
                }

                Interlocked.Decrement(ref parallelism);
            });

        foreach (var fp in handlerContext.FileSystem.EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories))
        {
            await c.Writer.WriteAsync(FilePair.FromFileEntry(fp), cancellationToken);
        }

        c.Writer.Complete();

        await pt;

        // 6. Remove PointerFileEntries that do not exist on disk
        handlerContext.StateRepo.DeletePointerFileEntries(pfe => /* note this was sfs first */ !handlerContext.FileSystem.FileExists(pfe.RelativeName));
    }

    private static Channel<T> GetBoundedChannel<T>(int capacity, bool singleWriter)
        => Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
            SingleWriter = singleWriter,
            SingleReader = false
        });

    private static ParallelOptions GetParallelOptions(int maxDegreeOfParallelism, CancellationToken cancellationToken = default)
        => new() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };

    private async Task UploadFileAsync(HandlerContext handlerContext, FilePair filePair, CancellationToken cancellationToken = default)
    {
        // 1. Hash the file
        var h = await handlerContext.Hasher.GetHashAsync(filePair);

        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
        var (needsToBeUploaded, uploadTask) = GetUploadStatus(handlerContext, h);

        // 3. Upload the Binary, if needed
        if (needsToBeUploaded)
        {
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
        var pf = filePair.GetOrCreatePointerFile(h);

        // 5. Write the PointerFileEntry
        handlerContext.StateRepo.UpsertPointerFileEntry(new PointerFileEntryDto
        {
            Hash = h.Value,
            RelativeName = pf.Path.FullName,
            CreationTimeUtc = pf.CreationTime,
            LastWriteTimeUtc = pf.LastWriteTime
        });
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

    //private PointerFile CreatePointerFile(BinaryFile bf, Hash h)
    //{
    //    var pf = bf.GetPointerFile();

    //    pf.Write(h, bf.CreationTime, bf.LastWriteTime);

    //    return pf;
    //}



    private class HandlerContext
    { 
        public HandlerContext(ArchiveCommand request)
        {
            Request = request;
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={request.AccountName};AccountKey={request.AccountKey};EndpointSuffix=core.windows.net";
            ContainerClient  = new BlobContainerClient(connectionString, request.ContainerName, new BlobClientOptions());

            StateRepo = new StateRepository();
            Hasher    = new SHA256Hasher(request.Passphrase);

            var pfs  = new PhysicalFileSystem();
            var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
            var sfs  = new SubFileSystem(pfs, root, true);
            FileSystem = new FilePairFileSystem(sfs, true);
        }

        public ArchiveCommand      Request         { get; }
        public BlobContainerClient ContainerClient { get; }
        public StateRepository     StateRepo       { get; }
        public SHA256Hasher        Hasher          { get; }
        public FilePairFileSystem  FileSystem      { get; }
    }
}