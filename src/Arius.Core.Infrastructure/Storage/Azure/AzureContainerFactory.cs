using System.Net;
using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.Infrastructure.Storage.Azure;

public sealed class AzureContainerFactory
{
    private readonly AzureRepositoryFactory         azureRepositoryFactory;
    private readonly ILogger<AzureContainerFactory> logger;

    public AzureContainerFactory(AzureRepositoryFactory azureRepositoryFactory, ILogger<AzureContainerFactory> logger)
    {
        this.azureRepositoryFactory = azureRepositoryFactory;
        this.logger                 = logger;
    }

    internal IContainer Create(AzureStorageAccount storageAccount, BlobServiceClient blobServiceClient, string containerName)
    {
        var bcc = blobServiceClient.GetBlobContainerClient(containerName);

        var r = bcc.CreateIfNotExists(PublicAccessType.None);
        if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
            logger.LogInformation("Container '{containerName}' created.", containerName);

        return new AzureContainer(storageAccount, bcc, azureRepositoryFactory);
    }
}