using Arius.Core.Domain.Services;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

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

    internal AzureRepository Create(BlobContainerClient blobContainerClient, string passphrase)
    {
        return new AzureRepository(blobContainerClient, passphrase, cryptoService, loggerFactory.CreateLogger<AzureRepository>());
    }
}