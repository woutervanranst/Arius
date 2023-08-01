using Arius.Core.Commands.Archive;
using Arius.Core.Queries;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Core.Facade;

public class NewFacade
{
    private readonly ILoggerFactory    loggerFactory;

    public NewFacade(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
    }

    public StorageAccountFacade ForStorageAccount(string storageAccountName, string storageAccountKey)
    {
        var storageAccountOptions = new StorageAccountOptions(storageAccountName, storageAccountKey);

        return new StorageAccountFacade(loggerFactory, storageAccountOptions);
    }
}



public class StorageAccountFacade
{
    private readonly ILoggerFactory        loggerFactory;
    private readonly IStorageAccountOptions storageAccountOptions;

    internal StorageAccountFacade(ILoggerFactory loggerFactory, IStorageAccountOptions options)
    {
        this.loggerFactory         = loggerFactory;
        this.storageAccountOptions = options;
    }

    public IAsyncEnumerable<string> GetContainerNamesAsync()
    {
        //var saq = services.GetRequiredService<StorageAccountQueries>();
        var saq = new StorageAccountQueries(loggerFactory.CreateLogger<StorageAccountQueries>(), storageAccountOptions);

        return saq.GetContainerNamesAsync();
    }

    public async Task<RepositoryFacade> ForRepositoryAsync(string containerName, string passphrase)
    {
        var ro = new RepositoryOptions(storageAccountOptions, containerName, passphrase);
        return await RepositoryFacade.CreateAsync(loggerFactory, ro);
    }
}



public class RepositoryFacade
{
    private readonly ILoggerFactory    loggerFactory;
    private readonly RepositoryOptions options;

    private RepositoryFacade(ILoggerFactory loggerFactory, RepositoryOptions options)
    {
        this.loggerFactory = loggerFactory;
        this.options       = options;
    }

    internal static async Task<RepositoryFacade> CreateAsync(ILoggerFactory loggerFactory, RepositoryOptions options)
    {
        var r = await new RepositoryBuilder(loggerFactory.CreateLogger<Repository>())
            .WithOptions(options)
            .WithLatestStateDatabase()
            .BuildAsync();

        return new RepositoryFacade(loggerFactory, options);
    }

    public IAsyncEnumerable<string> GetVersions()
    {
        throw new NotImplementedException();
    }

    public async Task<int> ExecuteArchiveCommandAsync(DirectoryInfo root, bool fastHash = false, bool removeLocal = false, AccessTier tier = default, bool dedup = false, DateTime versionUtc = default)
    {
        if (tier == default)
            tier = AccessTier.Cold;

        if (versionUtc == default)
            versionUtc = DateTime.UtcNow;

        var aco = new ArchiveCommandOptions(this.options, fastHash, removeLocal, tier, dedup, root, versionUtc);

        //TODO IArchiveCommandOptions.Validator

        var sp = new ArchiveCommandStatistics();

        var cmd = new ArchiveCommand(loggerFactory, sp);

        return await cmd.ExecuteAsync(aco);
    }
}