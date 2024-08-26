using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

public sealed class AzureContainerFactory
{
    private readonly AzureRepositoryFactory azureRepositoryFactory;

    public AzureContainerFactory(AzureRepositoryFactory azureRepositoryFactory)
    {
        this.azureRepositoryFactory = azureRepositoryFactory;
    }

    internal IContainer Create(AzureStorageAccount storageAccount, BlobServiceClient blobServiceClient, string containerName)
    {
        return new AzureContainer(storageAccount, blobServiceClient.GetBlobContainerClient(containerName), azureRepositoryFactory);
    }
}