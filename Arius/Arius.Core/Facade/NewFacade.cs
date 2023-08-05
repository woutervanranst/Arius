using Arius.Core.Commands.Archive;
using Arius.Core.Queries;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arius.Core.Commands.Rehydrate;
using Arius.Core.Commands.Restore;
using FluentValidation.Results;
using PostSharp.Constraints;
using PostSharp.Patterns.Contracts;
using System.Runtime.CompilerServices;

/*
 * This is required for the Arius.Cli.Tests module
 * Specifically, the Moq framework cannot initialize ICommand, which has '**internal** IServiceProvider Services { get; }' if it cannot see the internals
 * See https://stackoverflow.com/a/28235222/1582323
 */
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
[assembly: InternalsVisibleTo("Arius.Cli.Tests")]

/*
 * This is required to test the internals of the Arius.Core assembly
 */
[assembly: InternalsVisibleTo("Arius.Core.Tests")]
[assembly: InternalsVisibleTo("Arius.Core.Tests.Extensions")]
[assembly: InternalsVisibleTo("Arius.Core.BehaviorTests")]

namespace Arius.Core.Facade;

public class NewFacade
{
    private readonly ILoggerFactory    loggerFactory;

    [ComponentInternal("Arius.Cli.Tests")] // added only for Moq
    internal NewFacade()
    {
    }
    public NewFacade(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;

        //    var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
        //    {
        //        TempDirectoryName = ".ariustemp",
        //        RestoreTempDirectoryName = ".ariusrestore"
        //    });
    }


    public   virtual StorageAccountFacade ForStorageAccount([Required] string accountName, [Required] string accountKey) => ForStorageAccount(new StorageAccountOptions(accountName, accountKey));
    internal virtual StorageAccountFacade ForStorageAccount(IStorageAccountOptions storageAccountOptions)                => new(loggerFactory, storageAccountOptions);
}


public class StorageAccountFacade
{
    private readonly ILoggerFactory        loggerFactory;
    private readonly IStorageAccountOptions storageAccountOptions;

    [ComponentInternal("Arius.Cli.Tests")] // added only for Moq
    internal StorageAccountFacade()
    {
    }
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
    public   virtual async Task<RepositoryFacade> ForRepositoryAsync([Required] string containerName, [Required] string passphrase) => await ForRepositoryAsync(new RepositoryOptions(storageAccountOptions, containerName, passphrase));
    internal virtual async Task<RepositoryFacade> ForRepositoryAsync(IRepositoryOptions repositoryOptions)    => await RepositoryFacade.CreateAsync(loggerFactory, repositoryOptions);

    ///// <summary>
    ///// FOR UNIT TESTING PURPOSES ONLY
    ///// </summary>
    //internal async Task<RepositoryFacade> ForRepositoryAsync(string containerName, string passphrase, Repository.AriusDbContext mockedContext)
    //{
    //    var ro = new RepositoryOptions(storageAccountOptions, containerName, passphrase);
    //    return await RepositoryFacade.CreateAsync(loggerFactory, ro);
    //}
}



public class RepositoryFacade : IDisposable
{
    private readonly ILoggerFactory loggerFactory;

    [ComponentInternal("Arius.Cli.Tests")] // added only for Moq
    internal RepositoryFacade()
    {
    }
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

    public static ValidationResult ValidateArchiveCommandOptions(string accountName, string accountKey, string containerName, string passphrase, DirectoryInfo root, bool fastHash = false, bool removeLocal = false, AccessTier tier = default, bool dedup = false, DateTime versionUtc = default)
    {
        var v = new IArchiveCommandOptions.Validator();
        return v.Validate(new ArchiveCommandOptions(accountName, accountKey, containerName, passphrase, root, fastHash, removeLocal, tier, dedup, versionUtc));
    }

    public virtual async Task<(int, ArchiveCommandStatistics)> ExecuteArchiveCommandAsync(DirectoryInfo root, bool fastHash = false, bool removeLocal = false, AccessTier tier = default, bool dedup = false, DateTime versionUtc = default)
    {
        if (tier == default)
            tier = AccessTier.Cold;

        if (versionUtc == default)
            versionUtc = DateTime.UtcNow;

        var aco = new ArchiveCommandOptions(Repository, root, fastHash, removeLocal, tier, dedup, versionUtc);

        var sp = new ArchiveCommandStatistics();

        var cmd = new ArchiveCommand(loggerFactory, Repository, sp);

        var r = await cmd.ExecuteAsync(aco);

        return (r, sp);
    }

    public virtual async Task<int> ExecuteRestoreCommandAsync(DirectoryInfo root, bool synchronize = false, bool download = false, bool keepPointers = true, DateTime pointInTimeUtc = default)
    {
        if (pointInTimeUtc == default)
            pointInTimeUtc = DateTime.UtcNow;

        var rco = new RestoreCommandOptions(Repository, root, synchronize, download, keepPointers, pointInTimeUtc);

        // TODO IREstoreCommandOptions.Validator

        var cmd = new RestoreCommand(loggerFactory, Repository);

        return await cmd.ExecuteAsync(rco);
    }

    public async Task<int> ExecuteRehydrateCommandAsync()
    {
        var rco = new RehydrateCommandOptions(Repository);

        var cmd = new RehydrateCommand(loggerFactory.CreateLogger<RehydrateCommand>());

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

    [ComponentInternal("Arius.Cli.Tests")] // should be protected
    internal virtual void Dispose(bool disposing)
    {
        if (disposing)
            Repository.Dispose();
    }
}