using Arius.Core.Commands.Archive;
using Arius.Core.Commands.Rehydrate;
using Arius.Core.Commands.Restore;
using Arius.Core.Queries;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using PostSharp.Constraints;
using PostSharp.Patterns.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

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
[assembly: InternalsVisibleTo("Arius.Core.BehaviorTests")]

namespace Arius.Core.Facade;

public class Facade
{
    private readonly ILoggerFactory    loggerFactory;

    [ComponentInternal("Arius.Cli.Tests")] // added only for Moq
    internal Facade()
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


    public   virtual StorageAccountFacade ForStorageAccount([Required] string accountName, [Required] string accountKey) => ForStorageAccount(new StorageAccountOptions(accountName, accountKey));
    internal virtual StorageAccountFacade ForStorageAccount(IStorageAccountOptions storageAccountOptions)                => new(loggerFactory, storageAccountOptions);
}


public class StorageAccountFacade
{
    private readonly ILoggerFactory         loggerFactory;
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

    public IAsyncEnumerable<string> GetContainerNamesAsync(int maxRetries)
    {
        var saq = new StorageAccountQueries(loggerFactory.CreateLogger<StorageAccountQueries>(), storageAccountOptions);

        return saq.GetContainerNamesAsync(maxRetries);
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

    public string AccountName   => Repository.Options.AccountName;
    public string ContainerName => Repository.Options.ContainerName;


    // --------- ARCHIVE ---------
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


    // --------- RESTORE ---------
    public static ValidationResult ValidateRestoreCommandOptions(string accountName, string accountKey, string containerName, string passphrase, DirectoryInfo root, bool synchronize, bool download, bool keepPointers, DateTime? pointInTimeUtc)
    {
        var v = new IRestoreCommandOptions.Validator();
        return v.Validate(new RestoreCommandOptions(accountName, accountKey, containerName, passphrase, root, synchronize, download, keepPointers, pointInTimeUtc));
    }

    public virtual async Task<int> ExecuteRestoreCommandAsync(DirectoryInfo root, bool synchronize = false, bool download = false, bool keepPointers = true, DateTime pointInTimeUtc = default)
    {
        if (pointInTimeUtc == default)
            pointInTimeUtc = DateTime.UtcNow;

        var rco = new RestoreCommandOptions(Repository, root, synchronize, download, keepPointers, pointInTimeUtc);

        var cmd = new RestoreCommand(loggerFactory, Repository);

        return await cmd.ExecuteAsync(rco);
    }

    // --------- REHYDRATE ---------

    public async Task<int> ExecuteRehydrateCommandAsync()
    {
        throw new NotImplementedException();

        var rco = new RehydrateCommandOptions(Repository);

        var cmd = new RehydrateCommand(loggerFactory.CreateLogger<RehydrateCommand>());

        return await cmd.ExecuteAsync(rco);
    }

    // --------- QUERIES ---------

    public IAsyncEnumerable<string> GetVersions()
    {
        throw new NotImplementedException();
    }

    

    public async IAsyncEnumerable<(string RelativeParentPath, string DirectoryName, string Name)> GetEntriesAsync(
        string? relativeParentPathEquals = null,
        string? directoryNameEquals = null,
        string? nameContains = null)
    {
        var q = new RepositoryQueries(loggerFactory, Repository);
        await foreach (var e in q.GetEntriesAsync(relativeParentPathEquals, directoryNameEquals, nameContains))
            yield return e;
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



//public class Facade
//{
//    public async IAsyncEnumerable<IAriusEntry> GetLocalEntries(DirectoryInfo di)
//    {
//        var block = new IndexDirectoryBlockProvider(loggerFactory.CreateLogger<IndexDirectoryBlockProvider>()).GetBlock();

//        block.Post(di);
//        block.Complete();

//        while (await block.OutputAvailableAsync())
//        {
//            while (block.TryReceive(out var item))
//            {
//                yield return item;
//            }
//        }

//        await block.Completion.ConfigureAwait(false);
//    }

//    public StorageAccountFacade GetStorageAccountFacade(string accountName, string accountKey)
//    {
//        return new StorageAccountFacade(accountName, accountKey, loggerFactory);
//    }
//}

//public class StorageAccountFacade
//{
//    internal StorageAccountFacade(string accountName, string accountKey, ILoggerFactory loggerFactory)
//    {
//        if (string.IsNullOrEmpty(accountName))
//            throw new ArgumentException($"'{nameof(accountName)}' cannot be null or empty", nameof(accountName));
//        if (string.IsNullOrEmpty(accountKey))
//            throw new ArgumentException($"'{nameof(accountKey)}' cannot be null or empty", nameof(accountKey));

//        try
//        {
//            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

//            var blobServiceClient = new BlobServiceClient(connectionString);

//            var csa = CloudStorageAccount.Parse(connectionString);
//            var tableClient = csa.CreateCloudTableClient();

//            var tables = tableClient.ListTables().Select(ct => ct.Name).ToArray();

//            var r = blobServiceClient.GetBlobContainers()
//                .Where(bci => tables.Contains($"{bci.Name}{AzureRepository.TableNameSuffix}"))
//                .Select(bci => new ContainerFacade(accountName, accountKey, bci.Name, loggerFactory))
//                .ToArray();

//            Containers = r.ToArray();
//        }
//        catch (Exception e) when (e is FormatException || e is StorageException)
//        {
//            throw new ArgumentException("Invalid combination of Account Name / Key", e);
//        }
//    }

//    public IEnumerable<ContainerFacade> Containers { get; init; }
//}

//public class AzureRepositoryFacade
//{
//    internal AzureRepositoryFacade(string accountName, string accountKey, string containerName, string passphrase, ILoggerFactory loggerFactory)
//    {
//        if (string.IsNullOrEmpty(accountName))
//            throw new ArgumentException($"'{nameof(accountName)}' cannot be null or empty", nameof(accountName));
//        if (string.IsNullOrEmpty(accountKey))
//            throw new ArgumentException($"'{nameof(accountKey)}' cannot be null or empty", nameof(accountKey));
//        if (string.IsNullOrEmpty(containerName))
//            throw new ArgumentException($"'{nameof(containerName)}' cannot be null or empty", nameof(containerName));
//        if (string.IsNullOrEmpty(passphrase))
//            throw new ArgumentException($"'{nameof(passphrase)}' cannot be null or empty", nameof(passphrase));
//        if (loggerFactory is null)
//            throw new ArgumentException($"'{nameof(loggerFactory)}' cannot be null or empty", nameof(loggerFactory));

//        var aro = new AzureRepositoryOptions()
//        {
//            AccountName = accountName,
//            AccountKey = accountKey,
//            Container = containerName,
//            Passphrase = passphrase
//        };

//        this.loggerFactory = loggerFactory;

//        repository = GetRepo(aro);
//    }

//    private readonly AzureRepository repository;
//    private readonly ILoggerFactory loggerFactory;

//    private AzureRepository GetRepo(AzureRepositoryOptions aro)
//    {
//        var sc = new ServiceCollection()
//            .AddSingleton<ICommandExecutorOptions>(aro)
//            .AddSingleton<AzureRepository>()
//            .AddSingleton<Services.IBlobCopier, Services.AzCopier>()

//            .AddSingleton<ILoggerFactory>(loggerFactory)
//            .AddLogging()

//            .BuildServiceProvider();

//        return sc.GetRequiredService<AzureRepository>();
//    }

//    private class AzureRepositoryOptions : AzureRepository.IAzureRepositoryOptions, Services.IAzCopyUploaderOptions
//    {
//        public string AccountName { get; init; }
//        public string AccountKey { get; init; }
//        public string Container { get; init; }
//        public string Passphrase { get; init; }
//    }


//    /// <summary>
//    /// Get the versions (in universal time)
//    /// </summary>
//    /// <returns></returns>
//    public async Task<IEnumerable<DateTime>> GetVersionsAsync()
//    {
//        return await repository.GetVersionsAsync();
//    }

//    /// <summary>
//    /// Get the entries at the specified time (version IN UNIVERSAL TIME)
//    /// </summary>
//    public async IAsyncEnumerable<IAriusEntry> GetRemoteEntries(DateTime version, bool includeDeleted)
//    {
//        foreach (var item in await repository.GetEntries(version, includeDeleted))
//        {
//            yield return new PointerFileEntryAriusEntry(item);
//        }
//    }
//}

//public class PointerFileEntryAriusEntry : IAriusEntry
//{
//    internal PointerFileEntryAriusEntry(AzureRepository.PointerFileEntry pfe)
//    {
//        if (pfe is null)
//            throw new ArgumentNullException(nameof(pfe));

//        this.pfe = pfe;
//    }
//    private readonly AzureRepository.PointerFileEntry pfe;

//    public string RelativePath
//    {
//        get
//        {
//            var r = string.Join(Path.DirectorySeparatorChar, pfe.RelativeName.Split(Path.DirectorySeparatorChar).SkipLast(1));

//            if (string.IsNullOrEmpty(r))
//                return ".";
//            else
//                return r;
//        }
//    }

//    public string ContentName => pfe.RelativeName.Split(System.IO.Path.DirectorySeparatorChar).Last().TrimEnd(PointerFile.Extension);

//    public bool IsDeleted
//    {
//        get
//        {
//            return pfe.IsDeleted;
//        }
//    }
//}