using Arius.Core.Commands.Archive;
using Arius.Core.Queries;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arius.Core.Commands.Restore;
using PostSharp.Constraints;

namespace Arius.Core.Facade;

public class NewFacade
{
    private readonly ILoggerFactory    loggerFactory;

    public NewFacade(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;

        //    var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
        //    {
        //        TempDirectoryName = ".ariustemp",
        //        RestoreTempDirectoryName = ".ariusrestore"
        //    });
    }


    public   StorageAccountFacade ForStorageAccount(string storageAccountName, string storageAccountKey) => ForStorageAccount(new StorageAccountOptions(storageAccountName, storageAccountKey));
    internal StorageAccountFacade ForStorageAccount(IStorageAccountOptions storageAccountOptions)        => new(loggerFactory, storageAccountOptions);
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

    /// <summary>
    /// RECOMMEND TO CALL .Dispose() ON THE FACADE OR
    /// IN A USING BLOCK
    /// TO DELETE THE TEMPORARY DB
    /// </summary>
    /// <param name="containerName"></param>
    /// <param name="passphrase"></param>
    /// <returns></returns>
    public   async Task<RepositoryFacade> ForRepositoryAsync(string containerName, string passphrase) => await ForRepositoryAsync(new RepositoryOptions(storageAccountOptions, containerName, passphrase));
    internal async Task<RepositoryFacade> ForRepositoryAsync(IRepositoryOptions repositoryOptions)    => await RepositoryFacade.CreateAsync(loggerFactory, repositoryOptions);

    /// <summary>
    /// FOR UNIT TESTING PURPOSES ONLY
    /// </summary>
    internal async Task<RepositoryFacade> ForRepositoryAsync(string containerName, string passphrase, Repository.AriusDbContext mockedContext)
    {
        var ro = new RepositoryOptions(storageAccountOptions, containerName, passphrase);
        return await RepositoryFacade.CreateAsync(loggerFactory, ro);
    }
}



public class RepositoryFacade : IDisposable
{
    private readonly ILoggerFactory loggerFactory;

    private RepositoryFacade(ILoggerFactory loggerFactory, Repository repo)
    {
        Repository          = repo;
        this.loggerFactory = loggerFactory;
    }

    [ComponentInternal(typeof(StorageAccountFacade))]
    internal static async Task<RepositoryFacade> CreateAsync(ILoggerFactory loggerFactory, IRepositoryOptions options)
    {
        var repo = await new RepositoryBuilder(loggerFactory.CreateLogger<Repository>())
            .WithOptions(options)
            .WithLatestStateDatabase()
            .BuildAsync();

        return new RepositoryFacade(loggerFactory, repo);
    }

    internal Repository Repository { get; }

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

        var aco = new ArchiveCommandOptions(Repository, root, fastHash, removeLocal, tier, dedup, versionUtc);

        //TODO IArchiveCommandOptions.Validator

        var sp = new ArchiveCommandStatistics();

        var cmd = new ArchiveCommand(loggerFactory, Repository, sp);

        return await cmd.ExecuteAsync(aco);
    }

    public async Task<int> ExecuteRestoreCommandAsyc(DirectoryInfo root, bool synchronize = false, bool download = false, bool keepPointers = true, DateTime pointInTimeUtc = default)
    {
        if (pointInTimeUtc == default)
            pointInTimeUtc = DateTime.UtcNow;

        var rco = new RestoreCommandOptions(Repository, root, synchronize, download, keepPointers, pointInTimeUtc);

        // TODO IREstoreCommandOptions.Validator

        var cmd = new RestoreCommand(loggerFactory, Repository);

        return await cmd.ExecuteAsync(rco);
    }


    // --------- FINALIZER ---------

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~RepositoryFacade()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            Repository.Dispose();
    }
}