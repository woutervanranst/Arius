using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System.Net;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Azure Blob Storage implementation of the IStorage interface, providing blob storage operations
/// for managing containers and binary data in Microsoft Azure Storage accounts.
/// </summary>
internal class AzureBlobStorage : IStorage
{
    private readonly BlobContainerClient blobContainerClient;

    public AzureBlobStorage(string accountName, string accountKey, string containerName)
    {
        var blobServiceClient = new BlobServiceClient(
            new Uri($"https://{accountName}.blob.core.windows.net"),
            new StorageSharedKeyCredential(accountName, accountKey),
            new BlobClientOptions
            {
                Retry =
                {
                    Mode       = Azure.Core.RetryMode.Exponential,
                    Delay      = TimeSpan.FromSeconds(2),
                    MaxDelay   = TimeSpan.FromSeconds(16),
                    MaxRetries = 5
                }
            });
        blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    public async Task<bool> CreateContainerIfNotExistsAsync()
    {
        try
        {
            var result = await blobContainerClient.CreateIfNotExistsAsync(PublicAccessType.None);
            return result is not null && result.GetRawResponse().Status == (int)HttpStatusCode.Created;
        }
        catch (RequestFailedException e)
        {
            throw new InvalidOperationException($"Failed to create or access Azure Storage container '{blobContainerClient.Name}'. Please check your account credentials and permissions. See the log file for detailed error information.", e);
        }
    }

    public async Task<bool> ContainerExistsAsync()
    {
        try
        {
            return await blobContainerClient.ExistsAsync();
        }
        catch (RequestFailedException e)
        {
            throw new InvalidOperationException($"Failed to access Azure Storage container '{blobContainerClient.Name}'. Please check your account credentials and permissions. See the log file for detailed error information.", e);
        }
    }

    public IAsyncEnumerable<string> GetNamesAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return blobContainerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken)
            .Select(blob => blob.Name);
    }

    public async Task<Stream> OpenReadAsync(string blobName, IProgress<long>? progress = default, CancellationToken cancellationToken = default)
    {
        var blobClient = blobContainerClient.GetBlockBlobClient(blobName);
        return await blobClient.OpenReadAsync(cancellationToken: cancellationToken);

        // TODO Azure.RequestFailedException: 'Service request failed.
        // Status: 404 (The specified blob does not exist.)
        // ErrorCode: BlobNotFound
    }

    public async Task<Stream> OpenWriteAsync(string blobName, bool throwOnExists = false, IDictionary<string, string>? metadata = default, string? contentType = default, IProgress<long>? progress = default, CancellationToken cancellationToken = default)
    {
        var blobClient = blobContainerClient.GetBlockBlobClient(blobName);

        var options = new BlockBlobOpenWriteOptions();

        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        if (throwOnExists)
            options.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }; // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473

        if (metadata is not null)
            options.Metadata = metadata;

        if (contentType is not null)
            options.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };

        if (progress is not null)
            options.ProgressHandler = progress;

        return await blobClient.OpenWriteAsync(overwrite: !throwOnExists, options: options, cancellationToken: cancellationToken);
    }

    public async Task SetAccessTierAsync(string blobName, AccessTier tier)
    {
        var blobClient = blobContainerClient.GetBlobClient(blobName);
        await blobClient.SetAccessTierAsync(tier);
    }
}