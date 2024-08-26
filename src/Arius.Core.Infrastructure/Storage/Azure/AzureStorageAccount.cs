using Arius.Core.Domain.Storage;
using Azure.Storage;
using Azure.Storage.Blobs;
using System.Runtime.CompilerServices;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureStorageAccount : IStorageAccount
{
    private readonly AzureContainerFactory containerFactory;
    private readonly BlobServiceClient     blobServiceClient;
    
    public AzureStorageAccount(StorageAccountOptions storageAccountOptions, AzureContainerFactory containerFactory, BlobClientOptions options)
    {
        this.containerFactory = containerFactory;
        blobServiceClient = new BlobServiceClient(
            new Uri($"https://{storageAccountOptions.AccountName}.blob.core.windows.net"),
            new StorageSharedKeyCredential(storageAccountOptions.AccountName, storageAccountOptions.AccountKey),
            options);
    }

    public AzureStorageAccount(StorageAccountOptions storageAccountOptions, AzureContainerFactory containerFactory)
        : this(storageAccountOptions, containerFactory, new BlobClientOptions())
    {
    }


    public IContainer GetContainer(string containerName)
    {
        return containerFactory.Create(this, blobServiceClient, containerName);
    }

    public IAsyncEnumerable<IContainer> GetContainers([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        return blobServiceClient
            .GetBlobContainersAsync(cancellationToken: cancellationToken)
            .Select(containerItem => GetContainer(containerItem.Name));
    }
}