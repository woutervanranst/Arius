using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Azure.Core;
using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using WouterVanRanst.Utils;

namespace Arius.Core.Infrastructure.Repositories;

public class SqliteStateDbRepositoryFactory : IStateDbRepositoryFactory
{
    private readonly IStorageAccountFactory                  storageAccountFactory;
    private readonly ICryptoService                          cryptoService;
    private readonly AriusConfiguration                      config;
    private readonly ILogger<SqliteStateDbRepositoryFactory> logger;

    public SqliteStateDbRepositoryFactory(
        IStorageAccountFactory storageAccountFactory,
        ICryptoService cryptoService,
        IOptions<AriusConfiguration> config,
        ILogger<SqliteStateDbRepositoryFactory> logger)

    {
        this.storageAccountFactory = storageAccountFactory;
        this.cryptoService         = cryptoService;
        this.config                = config.Value;
        this.logger                = logger;
    }

    public async Task<IStateDbRepository> CreateAsync(RepositoryOptions repositoryOptions, RepositoryVersion? version = null)
    {
        // TODO Validation

        var repository = storageAccountFactory.GetRepository(repositoryOptions);

        async Task<string> DownloadRepo()
        {
            var stateDbFolder = config.GetStateDbForRepositoryName(repositoryOptions.ContainerName);

            if (version is null)
            {
                // Download the latest version
                var latestRepositoryVersion = await repository
                    .GetRepositoryVersions()
                    .OrderBy(b => b.Name)
                    .LastOrDefaultAsync();

                if (latestRepositoryVersion == null)
                {
                    // No states yet remotely - this is a fresh archive
                    return null;
                }   
                else
                {
                    // There is a state - is it cached locally?
                    var localPath = stateDbFolder.GetFullFileName(latestRepositoryVersion.Name);

                    if (File.Exists(localPath))
                    {
                        // Cached locally, ASSUME it s the same version
                        return localPath;
                    }
                    else
                    {
                        // Download the version locally
                        var blob = repository.GetRepositoryVersionBlob(latestRepositoryVersion);
                        await repository.DownloadAsync(blob, localPath, repositoryOptions.Passphrase);
                        return localPath;
                    }
                }
            }
            else
            {
                // Download the requested version
                try
                {
                    // Is it cached?
                    var localPath = stateDbFolder.GetFullFileName(version.Name);
                    if (File.Exists(localPath))
                    {
                        return localPath;
                    }
                    else
                    {
                        var blob = repository.GetRepositoryVersionBlob(version);
                        await repository.DownloadAsync(blob, localPath, repositoryOptions.Passphrase);
                        return localPath;
                    }
                }
                catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
                {
                    throw new ArgumentException("The requested version was not found", nameof(version), e);
                }
            }
        }
    }
}

public class SqliteStateDbRepository : IStateDbRepository
{

}