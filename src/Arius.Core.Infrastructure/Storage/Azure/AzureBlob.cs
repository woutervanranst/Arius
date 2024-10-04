using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Nito.AsyncEx;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal interface IAzureBlob : IBlob
{
    /// <summary>
    /// Refreshes the state of the object (the metadata)
    /// </summary>
    void Refresh();

    Task<long>        GetContentLengthAsync();
    Task<StorageTier> GetStorageTierAsync();
    Task              SetStorageTierAsync(StorageTier value);
    Task<string?>     GetContentTypeAsync();
    Task<bool>        ExistsAsync();
    Task              DeleteAsync();
    Task<long?>       GetOriginalContentLengthAsync();
    Task<Stream>      OpenReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Open the blob for writing.
    /// </summary>
    /// <param name="throwOnExists">If specified, and the blob already exists, a RequestFailedException with Status HttpStatusCode.Conflict is thrown</param>
    Task<Stream> OpenWriteAsync(string contentType = ICryptoService.ContentType, IDictionary<string, string>? metadata = default, bool throwOnExists = true, CancellationToken cancellationToken = default);
}

internal class AzureBlob : IAzureBlob
{
    private readonly BlobItem?                       blobItem;
    private readonly BlockBlobClient                 blockBlobClient;
    private          AsyncLazy<BlobCommonProperties> lazyCommonProperties;

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
        Refresh();
    }

    public AzureBlob(BlobItem blobItem, BlobContainerClient containerClient)
    {
        this.blobItem        = blobItem;
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

    /// <summary>
    /// Refreshes the state of the object (the metadata)
    /// </summary>
    public void Refresh()
    {
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

    public string FullName => blobItem?.Name ?? blockBlobClient!.Name;

    public string Name => Path.GetFileName(FullName);

    //public Uri Uri => blockBlobClient?.Uri ?? lazyBlobClient.Value.Result.Uri;

    public async Task<long> GetContentLengthAsync() => (await lazyCommonProperties).ContentLength;

    public async Task<StorageTier> GetStorageTierAsync()                  => (await lazyCommonProperties).AccessTier.ToStorageTier();
    public async Task              SetStorageTierAsync(StorageTier value) => await blockBlobClient.SetAccessTierAsync(value.ToAccessTier());


    public async   Task<string?> GetContentTypeAsync()             => (await lazyCommonProperties).ContentType;
    internal async Task          SetContentTypeAsync(string value) => await blockBlobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = value });


    public async Task<bool> ExistsAsync() => await blockBlobClient.ExistsAsync();


    public async Task DeleteAsync() => await blockBlobClient.DeleteIfExistsAsync();

    // --- METADATA

    public async Task<long?> GetOriginalContentLengthAsync()
    {
        var m = await GetMetadataAsync();

        if (m.TryGetValue(ORIGINAL_CONTENT_LENGTH_METADATA_KEY, out string? length))
            return long.Parse(length);
        else
            return null;
    }
    internal async Task<IDictionary<string, string>> GetMetadataAsync() => (await lazyCommonProperties).Metadata ?? new Dictionary<string, string>();
    internal async Task UpsertMetadata(string key, string value)
    {
        var m = await GetMetadataAsync();

        m[key] = value;

        await blockBlobClient.SetMetadataAsync(m);
    }

    internal const string ORIGINAL_CONTENT_LENGTH_METADATA_KEY = "OriginalContentLength";
    public static IDictionary<string, string> CreateChunkMetadata(long originalLength) 
        => new Dictionary<string, string> { { ORIGINAL_CONTENT_LENGTH_METADATA_KEY, originalLength.ToString() } };

    internal const string STATE_DATABASE_VERSION_METADATA_KEY = "DatabaseVersion";
    public static IDictionary<string, string> CreateStateDatabaseMetadata()
        => new Dictionary<string, string> { { STATE_DATABASE_VERSION_METADATA_KEY, "4" } };


    // --- STREAMS

    public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) => await blockBlobClient.OpenReadAsync(cancellationToken: cancellationToken);

    /// <summary>
    /// Open the blob for writing.
    /// </summary>
    /// <param name="throwOnExists">If specified, and the blob already exists, a RequestFailedException with Status HttpStatusCode.Conflict is thrown</param>
    public async Task<Stream> OpenWriteAsync(string contentType = ICryptoService.ContentType, IDictionary<string, string>? metadata = default, bool throwOnExists = true, CancellationToken cancellationToken = default)
    {
        var bbowo = new BlockBlobOpenWriteOptions();

        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        if (throwOnExists)
            bbowo.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }; // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
        if (metadata is not null)
            bbowo.Metadata = metadata;
        bbowo.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };
        
        return await blockBlobClient.OpenWriteAsync(overwrite: true, options: bbowo, cancellationToken: cancellationToken);
    }

    //public async Task<CopyFromUriOperation> StartCopyFromUriAsync(Uri source, BlobCopyFromUriOptions options)
    //{
    //    var client = await lazyBlobClient;
    //    return await client.StartCopyFromUriAsync(source, options);
    //}
}