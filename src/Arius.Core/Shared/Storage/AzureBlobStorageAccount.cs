using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Shared.Storage;

internal class AzureBlobStorageAccount
{
    private readonly ILogger<AzureBlobStorageAccount> logger;
    private readonly BlobServiceClient                blobServiceClient;

    public AzureBlobStorageAccount(string accountName, string accountKey, bool useRetryPolicy, ILogger<AzureBlobStorageAccount> logger)
    {
        this.logger = logger;

        blobServiceClient = new BlobServiceClient(
            new Uri($"https://{accountName}.blob.core.windows.net"),
            new StorageSharedKeyCredential(accountName, accountKey),
            GetBlobClientOptions(useRetryPolicy));

        try
        {
            // eager test connection
            blobServiceClient.GetProperties();
        }
        catch (RequestFailedException e)
        {
            throw new InvalidOperationException();
            //logger.LogError(e, "Failed to create or access Azure Storage container '{ContainerName}'. Status: {Status}, ErrorCode: {ErrorCode}", blobContainerClient.Name, e.Status, e.ErrorCode);
            //// Either invalid credentials ("No such host is known.") or invalid permissions ("Server failed to authenticate the request")
            //throw new InvalidOperationException($"Failed to create or access Azure Storage container '{blobContainerClient.Name}'. Please check your account credentials and permissions. See the log file for detailed error information.", e);
        }
    }

    public static BlobClientOptions GetBlobClientOptions(bool useRetryPolicy)
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

    public IAsyncEnumerable<string> GetContainerNames()
    {
        logger.LogInformation("Listing containers in storage account.");
        return blobServiceClient.GetBlobContainersAsync().Select(container => container.Name);
    }
}