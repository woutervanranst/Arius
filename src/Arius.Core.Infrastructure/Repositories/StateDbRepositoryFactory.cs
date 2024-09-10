using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Extensions;
using Azure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace Arius.Core.Infrastructure.Repositories;

public class SqliteStateDbRepositoryFactory : IStateDbRepositoryFactory
{
    private readonly IStorageAccountFactory                  storageAccountFactory;
    private readonly AriusConfiguration                      config;
    private readonly ILogger<SqliteStateDbRepositoryFactory> logger;

    public SqliteStateDbRepositoryFactory(
        IStorageAccountFactory storageAccountFactory,
        IOptions<AriusConfiguration> config,
        ILogger<SqliteStateDbRepositoryFactory> logger)

    {
        this.storageAccountFactory = storageAccountFactory;
        this.config                = config.Value;
        this.logger                = logger;
    }

    public async Task<IStateDbRepository> CreateAsync(RepositoryOptions repositoryOptions, RepositoryVersion? version = null)
    {
        await new RepositoryOptionsValidator().ValidateAndThrowAsync(repositoryOptions);

        var repository = storageAccountFactory.GetRepository(repositoryOptions);

        var (file, effectiveVersion) = await GetLocalStateDbFullNameAsync(repository, repositoryOptions, version);

        /* Database is locked -> Cache = shared as per https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
         *  NOTE if it still fails, try 'pragma temp_store=memory'
         *
         * Set command timeout to 60s to avoid concurrency errors on 'table is locked' 
         */
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        optionsBuilder.UseSqlite($"Data Source={file.FullName};Cache=Shared", sqliteOptions => { sqliteOptions.CommandTimeout(60); });

        return new StateDbRepository(optionsBuilder.Options, effectiveVersion);
    }

    private async Task<(IFile fullName, RepositoryVersion effectiveVersion)> GetLocalStateDbFullNameAsync(IRepository repository, RepositoryOptions repositoryOptions, RepositoryVersion? requestedVersion)
    {
        var localStateDbFolder = config.GetLocalStateDbFolderForRepository(repositoryOptions);

        if (requestedVersion is null)
        {
            var effectiveVersion = await GetLatestVersionAsync();
            if (effectiveVersion == null)
            {
                // No states yet remotely - this is a fresh archive
                effectiveVersion = DateTime.UtcNow;
                return (localStateDbFolder.GetFile(effectiveVersion.GetFileSystemName()), effectiveVersion);
            }
            return (await GetLocallyCachedAsync(repository, repositoryOptions, localStateDbFolder, effectiveVersion), effectiveVersion);
        }
        else
        {
            return (await GetLocallyCachedAsync(repository, repositoryOptions, localStateDbFolder, requestedVersion), requestedVersion);
        }

        async Task<RepositoryVersion?> GetLatestVersionAsync()
        {
            return await repository
                .GetRepositoryVersions()
                .OrderBy(b => b.Name)
                .LastOrDefaultAsync();
        }
    }

    private static async Task<IFile> GetLocallyCachedAsync(IRepository repository, RepositoryOptions repositoryOptions, DirectoryInfo stateDbFolder, RepositoryVersion version)
    {
        var localFile = stateDbFolder.GetFile(version.GetFileSystemName());

        if (localFile.Exists)
        {
            // Cached locally, ASSUME it’s the same version
            return localFile;
        }

        try
        {
            var blob = repository.GetRepositoryVersionBlob(version);
            await repository.DownloadAsync(blob, localFile);
            return localFile;
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

internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqliteStateDbContext> // used only for EF Core tools (e.g. dotnet ef migrations add ...)
{
    public SqliteStateDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        builder.UseSqlite();

        return new SqliteStateDbContext(builder.Options);
    }
}