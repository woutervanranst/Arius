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

internal interface IStorageAccountOptions
{
    string AccountName { get; }
    string AccountKey  { get; }
}

internal record StorageAccountOptions : IStorageAccountOptions
{
    public StorageAccountOptions(string accountName, string accountKey)
    {
        this.AccountName = accountName;
        this.AccountKey  = accountKey;
    }

    public string AccountName { get; }
    public string AccountKey  { get; }
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





internal interface IContainerOptions : IStorageAccountOptions
{
    string ContainerName { get; }
}

internal record ContainerOptions : IContainerOptions
{
    public ContainerOptions(IStorageAccountOptions storageAccountOptions, string containerName)
    {
        this.AccountName   = storageAccountOptions.AccountName;
        this.AccountKey    = storageAccountOptions.AccountKey;
        this.ContainerName = containerName;
    }

    public string AccountName   { get; }
    public string AccountKey    { get; }
    public string ContainerName { get; }
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

    public RepositoryFacade ForRepository(string passphrase)
    {
        var ro = new RepositoryOptions(ccontainerOptions, passphrase);
        return new RepositoryFacade(loggerFactory, ro);
    }
}



internal interface IRepositoryOptions2 : IContainerOptions
{
    string Passphrase { get; }
}

internal record RepositoryOptions : IRepositoryOptions2
{
    public RepositoryOptions(IContainerOptions containerOptions, string passphrase)
    {
        AccountName   = containerOptions.AccountName;
        AccountKey    = containerOptions.AccountKey;
        ContainerName = containerOptions.ContainerName;
        Passphrase    = passphrase;
    }

    public string AccountName { get; }
    public string AccountKey  { get; }
    public string ContainerName   { get; }
    public string Passphrase  { get; }
}

internal interface IArchiveCommandOptions2
{
    bool          FastHash    { get; }
    bool          RemoveLocal { get; }
    AccessTier    Tier        { get; }
    bool          Dedup       { get; }
    DirectoryInfo Path        { get; }
    DateTime      VersionUtc  { get; }
}

internal record ArchiveCommandOptions : RepositoryOptions, IArchiveCommandOptions2
{
    public ArchiveCommandOptions(RepositoryOptions repositoryOptions, bool fastHash, bool removeLocal, AccessTier tier, bool dedup, DirectoryInfo root, DateTime versionUtc) : base(repositoryOptions)
    {
        this.FastHash    = fastHash;
        this.RemoveLocal = removeLocal;
        this.Tier        = tier;
        this.Dedup       = dedup;
        this.Path        = root; // TODO rename to Root
        this.VersionUtc  = versionUtc;
    }

    public bool          FastHash    { get; }
    public bool          RemoveLocal { get; }
    public AccessTier    Tier        { get; }
    public bool          Dedup       { get; }
    public DirectoryInfo Path        { get; }
    public DateTime      VersionUtc  { get; }
}

public class RepositoryFacade
{
    private readonly ILoggerFactory    loggerFactory;
    private readonly RepositoryOptions options;

    internal RepositoryFacade(ILoggerFactory loggerFactory, RepositoryOptions options)
    {
        this.loggerFactory = loggerFactory;
        this.options       = options;
    }

    public IAsyncEnumerable<string> GetVersions()
    {
        throw new NotImplementedException();
    }

    public async Task<int> ExecuteArchiveCommand(IArchiveCommandOptions options, bool fastHash, bool removeLocal, AccessTier tier, bool dedup, DirectoryInfo root, DateTime versionUtc)
    {
        var                                            aco = new ArchiveCommandOptions(this.options, fastHash, removeLocal, tier, dedup, root, versionUtc);
        Core.Commands.ICommand<IArchiveCommandOptions> archiveCommand;

        return await archiveCommand.ExecuteAsync(options);
    }
}