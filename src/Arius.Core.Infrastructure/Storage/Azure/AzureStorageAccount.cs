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
    }

    public string AccountName => blobServiceClient.AccountName;

    public async IAsyncEnumerable<IContainer> ListContainersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var containerItem in blobServiceClient.GetBlobContainersAsync(cancellationToken: cancellationToken))
        {
            yield return new AzureContainer(blobServiceClient.GetBlobContainerClient(containerItem.Name));
        }
    }
}