using Arius.Core.Domain.Storage;
using Azure.Storage;
using Azure.Storage.Blobs;
using System.Runtime.CompilerServices;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureStorageAccount : IStorageAccount
{
    private readonly BlobServiceClient blobServiceClient;
    
    public AzureStorageAccount(StorageAccountOptions storageAccountOptions, BlobClientOptions options)
    {
        blobServiceClient = new BlobServiceClient(
            new Uri($"https://{storageAccountOptions.AccountName}.blob.core.windows.net"),
            new StorageSharedKeyCredential(storageAccountOptions.AccountName, storageAccountOptions.AccountKey),
            options);

        AccountKey = storageAccountOptions.AccountKey;
    }

    public AzureStorageAccount(StorageAccountOptions storageAccountOptions)
        : this(storageAccountOptions, new BlobClientOptions())
    {
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