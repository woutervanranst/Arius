using Arius.Core.Domain;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Arius.Core.Infrastructure.Storage.Azure;

public sealed class AzureRepositoryFactory
{
    private readonly IOptions<AriusConfiguration> config;
    private readonly ICryptoService               cryptoService;
    private readonly ILoggerFactory               loggerFactory;

    public AzureRepositoryFactory(IOptions<AriusConfiguration> config, ICryptoService cryptoService, ILoggerFactory loggerFactory)
    {
        this.config       = config;
        this.cryptoService = cryptoService;
        this.loggerFactory = loggerFactory;
    }

    internal AzureRemoteRepository Create(BlobContainerClient blobContainerClient, RemoteRepositoryOptions remoteRepositoryOptions)
    {
        return new AzureRemoteRepository(blobContainerClient, remoteRepositoryOptions, cryptoService, config, loggerFactory, loggerFactory.CreateLogger<AzureRemoteRepository>());
    }
}