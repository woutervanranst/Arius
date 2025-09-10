using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using FluentResults;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Azure Blob Storage implementation of the IStorage interface, providing blob storage operations
/// for managing containers and binary data in Microsoft Azure Storage accounts.
/// </summary>
internal class AzureBlobStorage : IStorage
{
    private readonly ILogger<AzureBlobStorage> logger;
    private readonly BlobContainerClient       blobContainerClient;

    public AzureBlobStorage(string accountName, string accountKey, string containerName, bool useRetryPolicy, ILogger<AzureBlobStorage> logger)
    {
        this.logger = logger;
        try
        {
            var blobServiceClient = new BlobServiceClient(
                new Uri($"https://{accountName}.blob.core.windows.net"),
                new StorageSharedKeyCredential(accountName, accountKey),
                GetBlobClientOptions());
            blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
        }
        catch (FormatException e)
        {
            throw new FormatException("Invalid account credentials format", e);
        }

        BlobClientOptions GetBlobClientOptions()
        {
            return useRetryPolicy
                ? new BlobClientOptions
                {
                    Retry =
                    {
                        Mode       = Azure.Core.RetryMode.Exponential,
                        Delay      = TimeSpan.FromSeconds(2),
                        MaxDelay   = TimeSpan.FromSeconds(16),
                        MaxRetries = 5
                    }
                }
                : new BlobClientOptions
                {
                    Retry =
                    {
                        MaxRetries = 0
                    }
                };
        }
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
            // Either invalid credentials ("No such host is known.") or invalid permissions ("Server failed to authenticate the request")
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

    public async Task<Result<Stream>> OpenReadAsync(string blobName, IProgress<long>? progress = default, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobClient = blobContainerClient.GetBlockBlobClient(blobName);
            var stream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
            return Result.Ok(stream);
        }
        catch (RequestFailedException e) when (e.BlobNotFound())
        {
            return Result.Fail(new BlobNotFoundError(blobName));
        }
        catch (RequestFailedException e) when (e.BlobIsArchived())
        {
            return Result.Fail(new BlobArchivedError(blobName));
        }
        catch (RequestFailedException e) when (e.BlobIsRehydrating())
        {
            return Result.Fail(new BlobRehydratingError(blobName));
        }
        catch (RequestFailedException e)
        {
            return Result.Fail(new Error($"Azure storage operation failed: {e.Message}")
                .WithMetadata("StatusCode", e.Status)
                .WithMetadata("ErrorCode", e.ErrorCode ?? "Unknown")
                .CausedBy(e));
        }
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

    public async Task StartHydrationAsync(string sourceBlobName, string targetBlobName, RehydrationPriority priority)
    {
        logger.LogInformation("Starting hydration from blob '{SourceBlob}' to '{TargetBlob}' with priority '{Priority}'", sourceBlobName, targetBlobName, priority);
        
        var source = blobContainerClient.GetBlobClient(sourceBlobName);
        var target = blobContainerClient.GetBlobClient(targetBlobName);

        var options = new BlobCopyFromUriOptions
        {
            AccessTier        = AccessTier.Cold,
            RehydratePriority = priority.ToRehydratePriority()
        };

        try
        {
            await target.StartCopyFromUriAsync(source.Uri, options);
            logger.LogInformation("Successfully started hydration from blob '{SourceBlob}' to '{TargetBlob}'", sourceBlobName, targetBlobName);
        }
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to start hydration from blob '{SourceBlob}' to '{TargetBlob}'. Status: {Status}, ErrorCode: {ErrorCode}", sourceBlobName, targetBlobName, e.Status, e.ErrorCode);
            throw;
        }
    }
}