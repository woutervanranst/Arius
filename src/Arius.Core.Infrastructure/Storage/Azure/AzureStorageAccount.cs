using Arius.Core.Domain.Storage;
using Azure.Storage;
using Azure.Storage.Blobs;
using System.Runtime.CompilerServices;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureStorageAccount : IStorageAccount
{
    private readonly BlobServiceClient blobServiceClient;

    public AzureStorageAccount(StorageAccountCredentials credentials)
    {
        blobServiceClient = new BlobServiceClient(new Uri($"https://{credentials.AccountName}.blob.core.windows.net"), new StorageSharedKeyCredential(credentials.AccountName, credentials.AccountKey));
        AccountKey        = credentials.AccountKey;
    }

    public string AccountName => blobServiceClient.AccountName;
    public string AccountKey  { get; }

    public async IAsyncEnumerable<IContainer> ListContainers([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var containerItem in blobServiceClient.GetBlobContainersAsync(cancellationToken: cancellationToken))
        {
            yield return new AzureContainer(this, blobServiceClient.GetBlobContainerClient(containerItem.Name));
        }
    }
}