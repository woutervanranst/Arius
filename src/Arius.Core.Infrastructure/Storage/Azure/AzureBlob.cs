using Arius.Core.Domain.Storage;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Nito.AsyncEx;

namespace Arius.Core.Infrastructure.Storage.Azure;

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

    public async Task SetContentTypeAsync(string value)
    {
        await blockBlobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = value });
    }

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
    public async Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default, bool throwOnExists = true)
    {
        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        if (throwOnExists)
            return await blockBlobClient.OpenWriteAsync(overwrite: true, options: new BlockBlobOpenWriteOptions
            {
                OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") } // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
                //,
                //HttpHeaders = new BlobHttpHeaders() { ContentType = }
            }, cancellationToken: cancellationToken);
        return await blockBlobClient.OpenWriteAsync(overwrite: true, cancellationToken: cancellationToken);
    }

    //public async Task<CopyFromUriOperation> StartCopyFromUriAsync(Uri source, BlobCopyFromUriOptions options)
    //{
    //    var client = await lazyBlobClient;
    //    return await client.StartCopyFromUriAsync(source, options);
    //}
}