using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.Core.Commands.Rehydrate;
using Arius.Core.Commands.Restore;
using Arius.Core.Extensions;
using Arius.Core.Queries.PointerFileEntriesSubdirectories;
using Arius.Core.Queries.PointerFilesEntries;
using Arius.Core.Queries.RepositoryStatistics;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using System.Collections.Generic;
using System.IO;
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
[assembly: InternalsVisibleTo("Arius.Core.Tests.Old")]
[assembly: InternalsVisibleTo("Arius.Core.New.UnitTests")]
[assembly: InternalsVisibleTo("Arius.Benchmarks")]
[assembly: InternalsVisibleTo("Arius.Core.BehaviorTests")]
[assembly: InternalsVisibleTo("Arius.ArchUnit")]

[assembly: InternalsVisibleTo("Arius.Core.DbMigrationV2V3")]

namespace Arius.Core.Facade;

public class Facade
{
    private readonly ILoggerFactory    loggerFactory;

    internal Facade() // added only for Moq
    {
    }
    public Facade(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;

        //    var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
        //    {
        //        TempDirectoryName = ".ariustemp",
        //        RestoreTempDirectoryName = ".ariusrestore"
        //    });
    }


    public   virtual StorageAccountFacade ForStorageAccount(string accountName, string accountKey) => ForStorageAccount(new StorageAccountOptions(accountName, accountKey));
    internal virtual StorageAccountFacade ForStorageAccount(StorageAccountOptions storageAccountOptions)                => new(loggerFactory, storageAccountOptions);
}


public class StorageAccountFacade
{
    private readonly ILoggerFactory        loggerFactory;
    private readonly StorageAccountOptions storageAccountOptions;
    
    internal StorageAccountFacade() // added only for Moq
    {
    }
    internal StorageAccountFacade(ILoggerFactory loggerFactory, StorageAccountOptions options)
    {
        this.loggerFactory         = loggerFactory;
        this.storageAccountOptions = options;
    }

    /// <summary>
    /// RECOMMEND TO CALL .Dispose() ON THE FACADE OR
    /// IN A USING BLOCK
    /// TO DELETE THE TEMPORARY DB
    /// </summary>
    /// <param name="containerName"></param>
    /// <param name="passphrase"></param>
    /// <returns></returns>
    public   virtual async Task<RepositoryFacade> ForRepositoryAsync(string containerName, string passphrase) => await ForRepositoryAsync(new RepositoryOptions(storageAccountOptions, containerName, passphrase));
    internal virtual async Task<RepositoryFacade> ForRepositoryAsync(RepositoryOptions repositoryOptions)    => await RepositoryFacade.CreateAsync(loggerFactory, repositoryOptions);

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

    internal RepositoryFacade() // added only for Moq
    {
    }
    private RepositoryFacade(ILoggerFactory loggerFactory, Repository repo)
    {
        Repository         = repo;
        this.loggerFactory = loggerFactory;
    }

    internal static async Task<RepositoryFacade> CreateAsync(ILoggerFactory loggerFactory, RepositoryOptions options) // [ComponentInternal(typeof(StorageAccountFacade))]
    {
        var repo = await new RepositoryBuilder(loggerFactory.CreateLogger<Repository>())
            .WithOptions(options)
            .WithLatestStateDatabase()
            .BuildAsync();

        return new RepositoryFacade(loggerFactory, repo);
    }

    /// <summary>
    /// FOR TESTING PURPOSES ONLY
    /// </summary>
    internal static RepositoryFacade Create(ILoggerFactory loggerFactory, Repository repo)
    {
        return new RepositoryFacade(loggerFactory, repo);
    }

    internal Repository Repository { get; }

    public string AccountName   => Repository.Options.AccountName;
    public string ContainerName => Repository.Options.ContainerName;


    // --------- ARCHIVE ---------

    /// <summary>
    /// Validate the given options for an Archive command
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException in case of an invalid option</exception>
    public static void ValidateArchiveCommandOptions(string accountName, string accountKey, string containerName, string passphrase, DirectoryInfo root, bool fastHash = false, bool removeLocal = false, string tier = default, bool dedup = false, DateTime versionUtc = default)
    {
        var o = new ArchiveCommand(accountName, accountKey, containerName, passphrase, root, fastHash, removeLocal, tier, dedup, versionUtc);
        o.Validate();
    }

    /// <summary>
    /// Execute an Archive command
    /// </summary>
    public virtual async Task<(CommandResultStatus, ArchiveCommandStatistics)> ExecuteArchiveCommandAsync(DirectoryInfo root, bool fastHash = false, bool removeLocal = false, string tier = default, bool dedup = false, DateTime versionUtc = default)
    {
        if (tier == default)
            tier = AccessTier.Cold.ToString();

        if (versionUtc == default)
            versionUtc = DateTime.UtcNow;

        var aco = new ArchiveCommand(Repository.Options, root, fastHash, removeLocal, tier, dedup, versionUtc);

        var sp = new ArchiveCommandStatistics();

        var cmd = new ArchiveCommandHandler(loggerFactory, Repository, sp);

        var r = await cmd.ExecuteAsync(aco);

        return (r, sp);
    }


    // --------- RESTORE ---------

    /// <summary>
    /// Validate the given options for a Restore command
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException in case of an invalid option</exception>
    public static void ValidateRestoreCommandOptions(string accountName, string accountKey, string containerName, string passphrase, DirectoryInfo root, bool synchronize, bool download, bool keepPointers, DateTime? pointInTimeUtc)
    {
        // TODO align handling of versionUtc == default and DateTime? pointInTime

        var o = new RestoreCommand(accountName, accountKey, containerName, passphrase, root, synchronize, download, keepPointers, pointInTimeUtc);
        o.Validate();
    }

    /// <summary>
    /// Execute a Restore command for a full directory
    /// </summary>
    public virtual async Task<CommandResultStatus> ExecuteRestoreCommandAsync(DirectoryInfo root, bool synchronize = false, bool download = false, bool keepPointers = true, DateTime pointInTimeUtc = default)
    {
        if (pointInTimeUtc == default)
            pointInTimeUtc = DateTime.UtcNow;

        var rco = new RestoreCommand(Repository.Options, root, synchronize, download, keepPointers, pointInTimeUtc);

        var cmd = new RestoreCommandHandler(loggerFactory, Repository);

        return await cmd.ExecuteAsync(rco);
    }

    /// <summary>
    /// Execute a Restore command for the specified files
    /// </summary>
    public async Task<CommandResultStatus> ExecuteRestoreCommandAsync(DirectoryInfo root, bool download = false, bool keepPointers = true, DateTime pointInTimeUtc = default, params string[] relativeNames)
    {
        if (pointInTimeUtc == default)
            pointInTimeUtc = DateTime.UtcNow;

        var rco = new RestorePointerFileEntriesCommand(Repository.Options, root, download, keepPointers, pointInTimeUtc, relativeNames);

        var cmd = new RestoreCommandHandler(loggerFactory, Repository);

        return await cmd.ExecuteAsync(rco);
    }

    // --------- REHYDRATE ---------

    public async Task<CommandResultStatus> ExecuteRehydrateCommandAsync()
    {
        throw new NotImplementedException();

        var rco = new RehydrateCommand(Repository);

        var cmd = new RehydrateCommandHandler(loggerFactory.CreateLogger<RehydrateCommandHandler>());

        return await cmd.ExecuteAsync(rco);
    }

    // --------- QUERIES ---------

    public IAsyncEnumerable<string> GetVersions()
    {
        throw new NotImplementedException();
    }

    

    public IAsyncEnumerable<IPointerFileEntryQueryResult> QueryPointerFileEntries(string? relativeDirectory = null)
    {
        var o = new PointerFileEntriesQuery { RelativeDirectory = relativeDirectory };
        var q = new PointerFileEntriesQueryHandler(loggerFactory, Repository);
        var r = q.Execute(o);

        return r.Result;
    }

    /// <summary>
    /// Get the directories in the PointerFileEntries that are prefixed with `prefix`
    /// For the root, use ""
    /// If the prefix is a precise folder, use a trailing slash, e.g. "dir1/dir2/", otherwise it will search until the next '/' which is 0 characters away
    /// </summary>
    /// <param name="prefix">The platform-specific prefix</param>
    /// <param name="depth">The depth in the directory structure to return</param>
    /// <param name="versionUtc">If specified, the version. If not specified will default to the latest version</param>
    /// <returns></returns>
    public IAsyncEnumerable<string> QueryPointerFileEntriesSubdirectories(string prefix, int depth, DateTime? versionUtc = default)
    {
        prefix = prefix.ToPlatformNeutralPath();
        var o = new PointerFileEntriesSubdirectoriesQuery { Prefix = prefix, Depth = depth, VersionUtc = versionUtc ?? DateTime.UtcNow };
        var q = new PointerFileEntriesSubdirectoriesQueryHandler(loggerFactory, Repository);
        var r = q.Execute(o);

        return r.Result.Select(r => r.ToPlatformSpecificPath());
    }

    public async Task<IQueryRepositoryStatisticsResult> QueryRepositoryStatisticsAsync()
    {
        var o = new RepositoryStatisticsQuery();
        var q = new RepositoryStatisticsQueryHandler(loggerFactory, Repository);
        var r = await q.ExecuteAsync(o);

        return r.Result;
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

    internal virtual void Dispose(bool disposing) // [ComponentInternal("Arius.Cli.Tests")] // should be protected
    {
        if (disposing)
            Repository.Dispose();
    }
}