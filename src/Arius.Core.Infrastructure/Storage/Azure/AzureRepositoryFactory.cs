using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

public sealed class AzureRepositoryFactory
{
    private readonly ICryptoService cryptoService;
    private readonly ILoggerFactory loggerFactory;

    public AzureRepositoryFactory(ICryptoService cryptoService, ILoggerFactory loggerFactory)
    {
        this.cryptoService = cryptoService;
        this.loggerFactory = loggerFactory;
    }

    internal AzureRemoteRepository Create(BlobContainerClient blobContainerClient, RemoteRepositoryOptions remoteRepositoryOptions)
    {
        return new AzureRemoteRepository(blobContainerClient, remoteRepositoryOptions, cryptoService, loggerFactory.CreateLogger<AzureRemoteRepository>());
    }
}