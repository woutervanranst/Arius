using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureContainer : IContainer
{
    private readonly AzureStorageAccount          storageAccount;
    private readonly BlobContainerClient          blobContainerClient;
    private readonly AzureRemoteRepositoryFactory azureRemoteRepositoryFactory;

    public AzureContainer(AzureStorageAccount storageAccount, BlobContainerClient blobContainerClient, AzureRemoteRepositoryFactory azureRemoteRepositoryFactory)
    {
        this.storageAccount               = storageAccount;
        this.blobContainerClient          = blobContainerClient;
        this.azureRemoteRepositoryFactory = azureRemoteRepositoryFactory;
    }

    public IStorageAccount StorageAccount => storageAccount;
    public string          Name           => blobContainerClient.Name;

    public IRemoteRepository GetRemoteRepository(RemoteRepositoryOptions remoteRepositoryOptions)
        => azureRemoteRepositoryFactory.Create(blobContainerClient, remoteRepositoryOptions);
}