using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.Core.Queries;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.Facade;

public static class Kak
{
    public static async Task x()
    {
        var f = new NewFacade(NullLoggerFactory.Instance);


        var cn = await saf.GetContainerNamesAsync().FirstAsync();

        var caf = saf.ForContainer(cn);
    }
}

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

    public ContainerFacade ForContainer(string containerName)
    {
        var co  = new ContainerOptions(storageAccountOptions, containerName);

        return new ContainerFacade(loggerFactory, co);
    }
}







public class ContainerFacade
{
    private readonly ILoggerFactory   loggerFactory;
    private readonly ContainerOptions ccontainerOptions;

    internal ContainerFacade(ILoggerFactory loggerFactory, ContainerOptions co)
    {
        this.loggerFactory     = loggerFactory;
        this.ccontainerOptions = co;
    }

    public async Task<RepositoryFacade> ForRepositoryAsync(string passphrase)
    {
        var ro = new RepositoryOptions(ccontainerOptions, passphrase);
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

    }

    public IAsyncEnumerable<string> GetVersions()
    {
        throw new NotImplementedException();
    }

    public async Task<int> ExecuteArchiveCommand(DirectoryInfo root, bool fastHash = false, bool removeLocal = false, AccessTier tier = default, bool dedup = false, DateTime versionUtc = default)
    {
        if (tier == default)
            tier = AccessTier.Cold;

        if (versionUtc == default)
            versionUtc = DateTime.UtcNow;

        var aco = new ArchiveCommandOptions(this.options, fastHash, removeLocal, tier, dedup, root, versionUtc);

        var sp = new ArchiveCommandStatistics();

        var cmd = new ArchiveCommand(loggerFactory, sp);

        return await cmd.ExecuteAsync(aco);
    }
}