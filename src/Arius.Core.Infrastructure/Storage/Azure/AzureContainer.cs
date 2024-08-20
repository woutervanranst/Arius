using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureContainer : IContainer
{
    private readonly AzureStorageAccount storageAccount;
    private readonly BlobContainerClient blobContainerClient;

    public AzureContainer(AzureStorageAccount storageAccount, BlobContainerClient blobContainerClient)
    {
        this.storageAccount      = storageAccount;
        this.blobContainerClient = blobContainerClient;
    }

    public IStorageAccount StorageAccount => storageAccount;
    public string          Name           => blobContainerClient.Name;

    public IRepository GetRepository(string passphrase)
    {
        return new AzureRepository(this, blobContainerClient, passphrase);
    }
}