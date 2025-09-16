using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using FluentResults;
using Microsoft.Extensions.Logging;
using System.Net;

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
            logger.LogInformation("Initializing Azure Blob Storage client for account '{AccountName}' and container '{ContainerName}' with retry policy: {UseRetryPolicy}", accountName, containerName, useRetryPolicy);
            var blobServiceClient = new BlobServiceClient(
                new Uri($"https://{accountName}.blob.core.windows.net"),
                new StorageSharedKeyCredential(accountName, accountKey),
                GetBlobClientOptions());
            blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
            logger.LogInformation("Azure Blob Storage client initialized successfully");
        }
        catch (FormatException e)
        {
            logger.LogError(e, "Failed to initialize Azure Blob Storage client due to invalid account credentials format");
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
            logger.LogInformation("Creating container '{ContainerName}' if it does not exist", blobContainerClient.Name);
            var result = await blobContainerClient.CreateIfNotExistsAsync(PublicAccessType.None);
            var wasCreated = result is not null && result.GetRawResponse().Status == (int)HttpStatusCode.Created;
            
            if (wasCreated)
                logger.LogInformation("Container '{ContainerName}' was created successfully", blobContainerClient.Name);
            else
                logger.LogInformation("Container '{ContainerName}' already exists", blobContainerClient.Name);
                
            return wasCreated;
        }
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to create or access Azure Storage container '{ContainerName}'. Status: {Status}, ErrorCode: {ErrorCode}", blobContainerClient.Name, e.Status, e.ErrorCode);
            // Either invalid credentials ("No such host is known.") or invalid permissions ("Server failed to authenticate the request")
            throw new InvalidOperationException($"Failed to create or access Azure Storage container '{blobContainerClient.Name}'. Please check your account credentials and permissions. See the log file for detailed error information.", e);
        }
    }

    public async Task<bool> ContainerExistsAsync()
    {
        try
        {
            logger.LogDebug("Checking if container '{ContainerName}' exists", blobContainerClient.Name);
            var exists = await blobContainerClient.ExistsAsync();
            logger.LogDebug("Container '{ContainerName}' exists: {Exists}", blobContainerClient.Name, exists);
            return exists;
        }
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to check if Azure Storage container '{ContainerName}' exists. Status: {Status}, ErrorCode: {ErrorCode}", blobContainerClient.Name, e.Status, e.ErrorCode);
            throw new InvalidOperationException($"Failed to access Azure Storage container '{blobContainerClient.Name}'. Please check your account credentials and permissions. See the log file for detailed error information.", e);
        }
    }

    public IAsyncEnumerable<string> GetNamesAsync(string prefix, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing blobs with prefix '{Prefix}' from container '{ContainerName}'", prefix, blobContainerClient.Name);
        return blobContainerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken)
            .Select(blob => blob.Name);
    }

    public async Task<Result<Stream>> OpenReadAsync(string blobName, IProgress<long>? progress = default, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Opening blob '{BlobName}' for reading from container '{ContainerName}'", blobName, blobContainerClient.Name);
        var blobClient = blobContainerClient.GetBlockBlobClient(blobName);

        try
        {
            var r = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            logger.LogDebug("Successfully opened blob '{BlobName}' for reading", blobName);

            return Result.Ok(r.Value.Content);
        }
        catch (RequestFailedException e) when (e is { Status: 404, ErrorCode: "BlobNotFound" })
        {
            logger.LogWarning("Blob '{BlobName}' not found in container '{ContainerName}'", blobName, blobContainerClient.Name);
            return Result.Fail(new BlobNotFoundError(blobName));
        }
        catch (RequestFailedException e) when (e is { Status: 409, ErrorCode: "BlobArchived" })
        {
            var p = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            // TODO zie tooltip van ArchiveStatus - only for LRS accounts?
            if (p.Value.ArchiveStatus?.StartsWith("rehydrate-pending-to-", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                logger.LogInformation("Blob '{BlobName}' is currently rehydrating", blobName);
                return Result.Fail(new BlobRehydratingError(blobName));
            }
            else if (p.Value.AccessTier == "Archive")
            {
                logger.LogInformation("Blob '{BlobName}' is archived and needs rehydration", blobName);
                return Result.Fail(new BlobArchivedError(blobName));
            }
            else
            {
                throw new InvalidOperationException();
            }
        }
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to open blob '{BlobName}' for reading. Status: {Status}, ErrorCode: {ErrorCode}", blobName, e.Status, e.ErrorCode);
            return Result.Fail(new Error($"Azure storage operation failed: {e.Message}")
                .WithMetadata("StatusCode", e.Status)
                .WithMetadata("ErrorCode", e.ErrorCode ?? "Unknown")
                .CausedBy(e));
        }
    }

    public async Task<Result<Stream>> OpenWriteAsync(string blobName, bool throwOnExists = false, IDictionary<string, string>? metadata = null, string? contentType = null, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Opening blob '{BlobName}' for writing in container '{ContainerName}', throwOnExists: {ThrowOnExists}", blobName, blobContainerClient.Name, throwOnExists);

        var blobClient = blobContainerClient.GetBlockBlobClient(blobName);

        var options = new BlockBlobOpenWriteOptions();

        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround, as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
        if (throwOnExists)
            options.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") };

        if (metadata is not null)
            options.Metadata = metadata;

        if (contentType is not null)
            options.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };

        if (progress is not null)
            options.ProgressHandler = progress;

        try
        {
            var stream = await blobClient.OpenWriteAsync(overwrite: true, options: options, cancellationToken: cancellationToken);
            logger.LogDebug("Successfully opened blob '{BlobName}' for writing", blobName);
            return Result.Ok(stream);
        }
        catch (RequestFailedException e) when (e is { Status: 409, ErrorCode: "BlobAlreadyExists" } or {Status: 409, ErrorCode: "BlobArchived" }) //icw ThrowOnExistOptions: throws this error when the blob already exists. In case of hot/cool, throws a 409+BlobAlreadyExists. In case of archive, throws a 409+BlobArchived
        {
            logger.LogInformation("Blob '{BlobName}' already exists", blobName);
            return Result.Fail(new BlobAlreadyExistsError(blobName));
        }
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to open blob '{BlobName}' for writing. Status: {Status}, ErrorCode: {ErrorCode}", blobName, e.Status, e.ErrorCode);
            return Result.Fail(new Error($"Azure storage operation failed: {e.Message}")
                .WithMetadata("StatusCode", e.Status)
                .WithMetadata("ErrorCode", e.ErrorCode ?? "Unknown")
                .CausedBy(e));
        }
    }

    public async Task<StorageProperties?> GetPropertiesAsync(string blobName, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting properties for blob '{BlobName}' from container '{ContainerName}'", blobName, blobContainerClient.Name);

        var blobClient = blobContainerClient.GetBlockBlobClient(blobName);

        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            logger.LogDebug("Successfully retrieved properties for blob '{BlobName}'", blobName);

            return new StorageProperties(
                ContentType: properties.Value.ContentType,
                Metadata: properties.Value.Metadata,
                StorageTier: properties.Value.AccessTier.ToStorageTier()
            );
        }
        catch (RequestFailedException e) when (e is { Status: 404, ErrorCode: "BlobNotFound" })
        {
            logger.LogDebug("Blob '{BlobName}' not found", blobName);
            return null;
        }
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to get properties for blob '{BlobName}'. Status: {Status}, ErrorCode: {ErrorCode}", blobName, e.Status, e.ErrorCode);
            throw;
        }
    }

    public async Task DeleteBlobAsync(string blobName, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Deleting blob '{BlobName}' from container '{ContainerName}'", blobName, blobContainerClient.Name);

        var blobClient = blobContainerClient.GetBlockBlobClient(blobName);

        try
        {
            await blobClient.DeleteAsync(cancellationToken: cancellationToken);
            logger.LogDebug("Successfully deleted blob '{BlobName}'", blobName);
        }
        //catch (RequestFailedException e) when (e is { Status: 404, ErrorCode: "BlobNotFound" })
        //{
        //    logger.LogDebug("Blob '{BlobName}' not found, nothing to delete", blobName);
        //    // Don't throw for not found - idempotent delete
        //}
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to delete blob '{BlobName}'. Status: {Status}, ErrorCode: {ErrorCode}", blobName, e.Status, e.ErrorCode);
            throw;
        }
    }

    public async Task SetAccessTierAsync(string blobName, AccessTier tier)
    {
        logger.LogInformation("Setting access tier for blob '{BlobName}' to '{AccessTier}'", blobName, tier);
        var blobClient = blobContainerClient.GetBlobClient(blobName);
        try
        {
            await blobClient.SetAccessTierAsync(tier);
            logger.LogInformation("Successfully set access tier for blob '{BlobName}' to '{AccessTier}'", blobName, tier);
        }
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to set access tier for blob '{BlobName}' to '{AccessTier}'. Status: {Status}, ErrorCode: {ErrorCode}", blobName, tier, e.Status, e.ErrorCode);
            throw;
        }
    }

    public async Task SetMetadataAsync(string blobName, IDictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Setting metadata for blob '{BlobName}'", blobName);
        var blobClient = blobContainerClient.GetBlobClient(blobName);
        try
        {
            await blobClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
            logger.LogDebug("Successfully set metadata for blob '{BlobName}'", blobName);
        }
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to set metadata for blob '{BlobName}'. Status: {Status}, ErrorCode: {ErrorCode}", blobName, e.Status, e.ErrorCode);
            throw;
        }
    }

    public async Task StartHydrationAsync(string sourceBlobName, string targetBlobName, RehydratePriority priority)
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
        catch (RequestFailedException e) when (e is { Status: 409, ErrorCode: "BlobArchived" } && target.Exists())
        {
            logger.LogWarning("Rehydration already started for blob '{SourceBlob}' to '{TargetBlob}'. Status: {Status}, ErrorCode: {ErrorCode}", sourceBlobName, targetBlobName, e.Status, e.ErrorCode);
        }
        catch (RequestFailedException e)
        {
            logger.LogError(e, "Failed to start hydration from blob '{SourceBlob}' to '{TargetBlob}'. Status: {Status}, ErrorCode: {ErrorCode}", sourceBlobName, targetBlobName, e.Status, e.ErrorCode);
            throw;
        }
    }
}