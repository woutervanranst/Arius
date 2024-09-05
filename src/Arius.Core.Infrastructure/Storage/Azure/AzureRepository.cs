using Arius.Core.Domain;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Nito.AsyncEx;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureRepository : IRepository
{
    private readonly BlobContainerClient      blobContainerClient;
    private readonly string                   passphrase;
    private readonly ICryptoService           cryptoService;
    private readonly ILogger<AzureRepository> logger;

    private readonly AzureContainerFolder StateFolder;
    private readonly AzureContainerFolder ChunkListsFolder;
    private readonly AzureContainerFolder ChunksFolder;
    private readonly AzureContainerFolder RehydratedChunksFolder;

    private const string STATE_DBS_FOLDER_NAME         = "states";
    private const string CHUNK_LISTS_FOLDER_NAME       = "chunklists";
    private const string CHUNKS_FOLDER_NAME            = "chunks";
    private const string REHYDRATED_CHUNKS_FOLDER_NAME = "chunks-rehydrated";

    public AzureRepository(BlobContainerClient blobContainerClient, string passphrase, ICryptoService cryptoService, ILogger<AzureRepository> logger)
    {
        this.blobContainerClient = blobContainerClient;
        this.passphrase          = passphrase;
        this.cryptoService       = cryptoService;
        this.logger              = logger;

        StateFolder            = new AzureContainerFolder(blobContainerClient, STATE_DBS_FOLDER_NAME);
        ChunkListsFolder       = new AzureContainerFolder(blobContainerClient, CHUNK_LISTS_FOLDER_NAME);
        ChunksFolder           = new AzureContainerFolder(blobContainerClient, CHUNKS_FOLDER_NAME);
        RehydratedChunksFolder = new AzureContainerFolder(blobContainerClient, REHYDRATED_CHUNKS_FOLDER_NAME);
    }

    public IAsyncEnumerable<RepositoryVersion> GetRepositoryVersions()
    {
        return StateFolder.GetBlobs().Select(blob => new RepositoryVersion { Name = blob.Name });
    }

    public IBlob GetRepositoryVersionBlob(RepositoryVersion repositoryVersion)
    {
        return StateFolder.GetBlob(repositoryVersion.Name);
    }

    public async Task<BinaryProperties> UploadChunkAsync(IBinaryFileWithHash file, CancellationToken cancellationToken = default)
    {
        var b = ChunksFolder.GetBlob(file.Hash.Value.BytesToHexString());

        var r = await UploadAsync(file, b, cancellationToken);

        var bp = new BinaryProperties
        {
            Hash              = file.Hash,
            OriginalLength    = r.originalLength,
            ArchivedLength    = r.archivedLength,
            IncrementalLength = 0, // TODO
            StorageTier       = StorageTier.Hot // TODO
        };


        // TODO Set contenttype, metadata, accesstier, ...

        return bp;
    }

    private async Task<(long originalLength, long archivedLength)> UploadAsync(IFile source, AzureBlob target, CancellationToken cancellationToken = default)
    {
        await using var ts = await target.OpenWriteAsync();
        await using var ss = source.OpenRead();
        await cryptoService.CompressAndEncryptAsync(ss, ts, passphrase);

        return (ss.Length, ts.Position); // ts.Length is not supported
    }

    public async Task DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default)
    {
        await using var ss = await blob.OpenReadAsync(cancellationToken);
        await using var ts = file.OpenWrite();
        await cryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);

        logger.LogInformation("Successfully downloaded latest state '{blob}' to '{file}'", blob.Name, file);
    }
}

internal class AzureContainerFolder
{
    private readonly BlobContainerClient blobContainerClient;
    private readonly string              folderName;

    public AzureContainerFolder(BlobContainerClient blobContainerClient, string folderName)
    {
        this.blobContainerClient = blobContainerClient;
        this.folderName          = folderName;
    }

    public IAsyncEnumerable<AzureBlob> GetBlobs()
    {
        return blobContainerClient
            .GetBlobsAsync(prefix: $"{folderName}/")
            .Select(bi => new AzureBlob(bi, blobContainerClient));
    }

    public AzureBlob GetBlob(string name)
    {
        return new AzureBlob(blobContainerClient.GetBlockBlobClient($"{folderName}/{name}"));
    }
}

internal class AzureBlob : IBlob
{
    private readonly BlobItem?                       blobItem;
    private readonly BlockBlobClient                 blockBlobClient;
    private readonly AsyncLazy<BlobCommonProperties> lazyCommonProperties;

    internal record BlobCommonProperties
    {
        public long                         ContentLength { get; init; }
        public AccessTier?                  AccessTier    { get; init; }
        public string?                      ContentType   { get; init; }
        public IDictionary<string, string>? Metadata      { get; init; }
    }

    public AzureBlob(BlockBlobClient blockBlobClient)
    {
        this.blockBlobClient = blockBlobClient;
        lazyCommonProperties = new AsyncLazy<BlobCommonProperties>(async () =>
        {
            var properties = await blockBlobClient.GetPropertiesAsync();
            return new BlobCommonProperties
            {
                ContentLength = properties.Value.ContentLength,
                AccessTier    = properties.Value.AccessTier,
                ContentType   = properties.Value.ContentType,
                Metadata      = properties.Value.Metadata
            };
        });
    }

    public AzureBlob(BlobItem blobItem, BlobContainerClient containerClient)
    {
        this.blobItem = blobItem;
        this.blockBlobClient = containerClient.GetBlockBlobClient(blobItem.Name);

        // Lazy initialization of commonProperties using blobItem.Properties
        lazyCommonProperties = new AsyncLazy<BlobCommonProperties>(() => Task.FromResult(new BlobCommonProperties
        {
            ContentLength = blobItem.Properties.ContentLength ?? 0,
            AccessTier    = blobItem.Properties.AccessTier,
            ContentType   = blobItem.Properties.ContentType,
            Metadata      = blobItem.Metadata
        }));
    }

    public string FullName => blobItem?.Name ?? blockBlobClient!.Name;

    public string Name => Path.GetFileName(FullName);

    //public Uri Uri => blockBlobClient?.Uri ?? lazyBlobClient.Value.Result.Uri;

    public async Task<long> GetContentLengthAsync() => (await lazyCommonProperties).ContentLength;

    public async Task<StorageTier> GetStorageTierAsync() => (await lazyCommonProperties).AccessTier.ToStorageTier();

    public async Task SetStorageTierAsync(StorageTier value)
    {
        await blockBlobClient.SetAccessTierAsync(value.ToAccessTier());
    }

    //public async Task<string?> GetContentTypeAsync() => (await lazyCommonProperties).ContentType;

    //public async Task SetContentTypeAsync(string value)
    //{
    //    var client = await lazyBlobClient;
    //    await client.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = value });
    //}

    //public async Task<IDictionary<string, string>> GetMetadataAsync() => (await lazyCommonProperties).Metadata ?? new Dictionary<string, string>();

    public async Task<bool> ExistsAsync()
    {
        return await blockBlobClient.ExistsAsync();
    }

    //public async Task DeleteAsync()
    //{
    //    var client = await lazyBlobClient;
    //    await client.DeleteIfExistsAsync();
    //}

    public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        return await blockBlobClient.OpenReadAsync(cancellationToken:cancellationToken);
    }

    /// <summary>
    /// Open the blob for writing.
    /// </summary>
    /// <param name="throwOnExists">If specified, and the blob already exists, a RequestFailedException with Status HttpStatusCode.Conflict is thrown</param>
    public async Task<Stream> OpenWriteAsync(bool throwOnExists = true)
    {
        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        if (throwOnExists)
            return await blockBlobClient.OpenWriteAsync(overwrite: true, options: new BlockBlobOpenWriteOptions
            {
                OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") } // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
            });
        return await blockBlobClient.OpenWriteAsync(overwrite: true);
    }

    //public async Task<CopyFromUriOperation> StartCopyFromUriAsync(Uri source, BlobCopyFromUriOptions options)
    //{
    //    var client = await lazyBlobClient;
    //    return await client.StartCopyFromUriAsync(source, options);
    //}
}