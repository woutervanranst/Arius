using Microsoft.Extensions.Logging;
using System;
using System.Runtime.CompilerServices;



namespace Arius.Core.Facade;

public class Facade //: IFacade
{
    public Facade(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this.loggerFactory            = loggerFactory;
    }

    private readonly ILoggerFactory           loggerFactory;
}


//public ICommand CreateDedupEvalCommand(string path)
    //{
    //    var options = new DedupEvalCommandOptions { Root = new DirectoryInfo(path) };

    //    var sp = CreateServiceProvider(loggerFactory, tempDirectoryAppSettings, options);

    //    var dec = sp.GetRequiredService<DedupEvalCommand>();

    //    return dec;

    //}
    //public ICommand CreateArchiveCommand(string accountName, string accountKey, string passphrase, bool fastHash, string container, bool removeLocal, string tier, bool dedup, string path, DateTime versionUtc)
    //{
    //    throw new NotImplementedException();

    //    //var options = new ArchiveCommandOptions(accountName, accountKey, passphrase, fastHash, container, removeLocal, tier, dedup, path, versionUtc);

    //    //var sp = CreateServiceProvider(loggerFactory, tempDirectoryAppSettings, options);

    //    //var ac = sp.GetRequiredService<ArchiveCommand>();

    //    //return ac;
    //}

    //public ICommand CreateRestoreCommand(string accountName, string accountKey, string container, string passphrase, bool synchronize, bool download, bool keepPointers, string path, DateTime pointInTimeUtc)
    //{
    //    var options = new RestoreCommandOptions(accountName, accountKey, container, passphrase, synchronize, download, keepPointers, path, pointInTimeUtc);

    //    var sp = CreateServiceProvider(loggerFactory, tempDirectoryAppSettings, options);

    //    var rc = sp.GetRequiredService<RestoreCommand>();

    //    return rc;
    //}

    //internal ServiceProvider GetServices(string accountName, string accountKey, string container, string passphrase)
    //{
    //    var options = new RepositoryOptions(accountName, accountKey, container, passphrase);

    //    return CreateServiceProvider(loggerFactory, tempDirectoryAppSettings, options);
    //}



//public class Facade
//{
//    public Facade(ILoggerFactory loggerFactory)
//    {
//        this.loggerFactory = loggerFactory;
//    }

//    private readonly ILoggerFactory loggerFactory;

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

//public class ContainerFacade
//{
//    internal ContainerFacade(string accountName, string accountKey, string containerName, ILoggerFactory loggerFactory)
//    {
//        if (string.IsNullOrEmpty(accountName))
//            throw new ArgumentException($"'{nameof(accountName)}' cannot be null or empty", nameof(accountName));
//        if (string.IsNullOrEmpty(accountKey))
//            throw new ArgumentException($"'{nameof(accountKey)}' cannot be null or empty", nameof(accountKey));
//        if (string.IsNullOrEmpty(containerName))
//            throw new ArgumentException($"'{nameof(containerName)}' cannot be null or empty", nameof(containerName));
//        if (loggerFactory is null)
//            throw new ArgumentException($"'{nameof(loggerFactory)}' cannot be null or empty", nameof(loggerFactory));

//        this.accountName = accountName;
//        this.accountKey = accountKey;
//        this.containerName = containerName;
//        this.loggerFactory = loggerFactory;
//    }

//    private readonly string accountName;
//    private readonly string accountKey;
//    private readonly string containerName;
//    private readonly ILoggerFactory loggerFactory;

//    public string Name => containerName;

//    public AzureRepositoryFacade GetAzureRepositoryFacade(string passphrase)
//    {
//        return new AzureRepositoryFacade(accountName, accountKey, containerName, passphrase, loggerFactory);
//    }
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