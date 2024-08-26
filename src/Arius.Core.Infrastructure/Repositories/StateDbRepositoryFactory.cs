using System.Net.Http.Headers;
using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        var fullName = await GetLocalRepositoryFullName();

        return new SqliteStateDbRepository(fullName);


        async Task<string> GetLocalRepositoryFullName()
        {
            var localStateDbFolder = config.GetLocalStateDbFolderForRepositoryName(repositoryOptions.ContainerName);

            if (version is null)
            {
                var latestVersion = await GetLatestVersionAsync();
                if (latestVersion == null)
                {
                    // No states yet remotely - this is a fresh archive
                    return localStateDbFolder.GetFullFileName($"{DateTime.UtcNow:s}");
                }
                return await GetLocallyCachedAsync(localStateDbFolder, latestVersion);
            }
            else
            {
                return await GetLocallyCachedAsync(localStateDbFolder, version);
            }

            async Task<RepositoryVersion?> GetLatestVersionAsync()
            {
                return await repository
                    .GetRepositoryVersions()
                    .OrderBy(b => b.Name)
                    .LastOrDefaultAsync();
            }

            async Task<string> GetLocallyCachedAsync(DirectoryInfo stateDbFolder, RepositoryVersion version)
            {
                var localPath = stateDbFolder.GetFullFileName(version.Name);

                if (File.Exists(localPath))
                {
                    // Cached locally, ASSUME it’s the same version
                    return localPath;
                }

                try
                {
                    var blob = repository.GetRepositoryVersionBlob(version);
                    await repository.DownloadAsync(blob, localPath, repositoryOptions.Passphrase);
                    return localPath;
                }
                catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
                {
                    throw new ArgumentException("The requested version was not found", nameof(version), e);
                }
                catch (InvalidDataException e)
                {
                    throw new ArgumentException("Could not load the state database. Probably a wrong passphrase was used.", e);
                }
            }
        }
    }
}

public class SqliteStateDbRepository : DbContext, IStateDbRepository
{
    internal SqliteStateDbRepository(string localPath)
    {
    }

}