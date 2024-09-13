using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureContainer : IContainer
{
    private readonly AzureStorageAccount    storageAccount;
    private readonly BlobContainerClient    blobContainerClient;
    private readonly AzureRepositoryFactory azureRepositoryFactory;

    public AzureContainer(AzureStorageAccount storageAccount, BlobContainerClient blobContainerClient, AzureRepositoryFactory azureRepositoryFactory)
    {
        this.storageAccount         = storageAccount;
        this.blobContainerClient    = blobContainerClient;
        this.azureRepositoryFactory = azureRepositoryFactory;
    }

    public IStorageAccount StorageAccount => storageAccount;
    public string          Name           => blobContainerClient.Name;

    public ICloudRepository GetRepository(string passphrase)
    {
        return azureRepositoryFactory.Create(blobContainerClient, passphrase);
    }
}