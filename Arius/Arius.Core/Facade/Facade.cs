using Arius.Core.Commands;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

/*
 * This is required for the Arius.Cli.Tests module
 * Specifically, the Moq framework cannot initialize ICommand, which has 'internal IServiceProvider Services { get; }' if it cannot see the internals
 * See https://stackoverflow.com/a/28235222/1582323
 */
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

/*
 * This is required to test the internals of the Arius.Core assembly
 */
[assembly: InternalsVisibleTo("Arius.Core.Tests")]
namespace Arius.Core.Facade
{
    public interface IFacade
    {
        ICommand CreateRestoreCommand(string accountName, string accountKey, string container, string passphrase, bool synchronize, bool download, bool keepPointers, string path);
    }
    public partial class Facade : IFacade
    {
        internal interface IOptions // Used for DI in the facade
        {
        }

        public Facade(ILoggerFactory loggerFactory,
            IOptions<AzCopyAppSettings> azCopyAppSettings, IOptions<TempDirectoryAppSettings> tempDirectoryAppSettings)
        {
            if (loggerFactory is null)
                throw new ArgumentNullException(nameof(loggerFactory));
            if (azCopyAppSettings is null)
                throw new ArgumentNullException(nameof(azCopyAppSettings));
            if (tempDirectoryAppSettings is null)
                throw new ArgumentNullException(nameof(tempDirectoryAppSettings));

            this.loggerFactory = loggerFactory;
            this.azCopyAppSettings = azCopyAppSettings.Value;
            this.tempDirectoryAppSettings = tempDirectoryAppSettings.Value;

            //services = new(() => InitializeServiceProvider(loggerFactory, azCopyAppSettings.Value, tempDirectoryAppSettings.Value));
        }

        public ArchiveCommandBuilder GetArchiveCommandBuilder()
        {
            return new ArchiveCommandBuilder((options) =>
            {
                var sp = CreateServiceProvider(loggerFactory, azCopyAppSettings, tempDirectoryAppSettings, options);

                var ace = sp.GetRequiredService<ArchiveCommand>();

                return ace;
            });
        }




        private readonly ILoggerFactory loggerFactory;
        private readonly AzCopyAppSettings azCopyAppSettings;
        private readonly TempDirectoryAppSettings tempDirectoryAppSettings;

        public ICommand CreateRestoreCommand(string accountName, string accountKey, string container,
            string passphrase, bool synchronize, bool download, bool keepPointers, string path)
        {
            var options = new RestoreCommandOptions
            {
                AccountName = accountName,
                AccountKey = accountKey,
                Passphrase = passphrase,
                Container = container,
                Synchronize = synchronize,
                Download = download,
                KeepPointers = keepPointers,
                Path = path
            };

            var sp = CreateServiceProvider(loggerFactory, azCopyAppSettings, tempDirectoryAppSettings, options);

            var rce = sp.GetRequiredService<RestoreCommand>();

            return rce;
        }


        private static ServiceProvider CreateServiceProvider<T>(ILoggerFactory loggerFactory,
            AzCopyAppSettings azCopyAppSettings, TempDirectoryAppSettings tempDirectoryAppSettings,
            T options) where  T : IOptions
        {
            var sc = new ServiceCollection();

            sc
                //Add Commmands
                .AddSingleton<ArchiveCommand>()
                .AddSingleton<RestoreCommand>()

                //Add Services
                .AddSingleton<PointerService>()
                .AddSingleton<IHashValueProvider, SHA256Hasher>()
                .AddSingleton<IEncrypter, SevenZipCommandlineEncrypter>()
                .AddSingleton<IBlobCopier, AzCopier>()
                .AddSingleton<AzureRepository>()

                // Add Chunkers
                .AddSingleton<Chunker>()
                .AddSingleton<DedupChunker>();

            if (options is IChunker.IOptions chunkerOptions)
            { 
                sc
                    .AddSingleton<IChunker>((sp) =>
                    {
                        if (chunkerOptions.Dedup)
                            return sp.GetRequiredService<DedupChunker>();
                        else
                            return sp.GetRequiredService<Chunker>();
                    });
            }
            //else
            //{
            //    throw new NotImplementedException();
            //}

            // Add Options
            sc
                //sc.AddOptions<AzCopyAppSettings>().Bind().Configure((a) => a.() => azCopyAppSettings);
                //sc.AddOptions<TempDirectoryAppSettings>(() => tempDirectoryAppSettings);
                //.AddOptions<IAzCopyAppSettings>().Bind(hostBuilderContext.Configuration.GetSection("AzCopier"));
                //services.AddOptions<ITempDirectoryAppSettings>().Bind(hostBuilderContext.Configuration.GetSection("TempDir"));
                .AddSingleton(azCopyAppSettings)
                .AddSingleton(tempDirectoryAppSettings);


            foreach (var type in options.GetType().GetInterfaces())
                sc.AddSingleton(type, options);

            ////Add the options for the Repositories
            //sc
            //    .AddSingleton<AzureRepository.IOptions>(options);

            //// Add the options for the services
            //sc
            //    .AddSingleton<IBlobCopier.IOptions>(options)
            //    .AddSingleton<IEncrypter.IOptions>(options)
            //    .AddSingleton<IHashValueProvider.IOptions>(options);

            ArchiveCommand.ConfigureServices(sc);
            //ArchiveCommandExecutor.AddOptions(sc, accountName, accountKey, passphrase, fastHash, container, removeLocal, tier, path);
            RestoreCommand.ConfigureServices(sc);

            sc
                .AddSingleton<ILoggerFactory>(loggerFactory)
                .AddLogging();

            return sc.BuildServiceProvider();
        }

        internal AzureRepository GetAriusRepository<T>(T options) where T : AzureRepository.IOptions, IBlobCopier.IOptions
        {
            var sc = new ServiceCollection();

            sc
                //Add Services
                .AddSingleton<IBlobCopier, AzCopier>()
                .AddSingleton<AzureRepository>();

            // Add Options
            sc
                //sc.AddOptions<AzCopyAppSettings>().Bind().Configure((a) => a.() => azCopyAppSettings);
                //sc.AddOptions<TempDirectoryAppSettings>(() => tempDirectoryAppSettings);
                //.AddOptions<IAzCopyAppSettings>().Bind(hostBuilderContext.Configuration.GetSection("AzCopier"));
                //services.AddOptions<ITempDirectoryAppSettings>().Bind(hostBuilderContext.Configuration.GetSection("TempDir"));
                .AddSingleton(azCopyAppSettings)
                .AddSingleton(tempDirectoryAppSettings);

            // Add the options for the services
            sc
                .AddSingleton<IBlobCopier.IOptions>(options);

            sc
                .AddSingleton<ILoggerFactory>(loggerFactory)
                .AddLogging();

            return sc.BuildServiceProvider()
                .GetRequiredService<AzureRepository>();
        }
    }


    //internal interface ICommand
    //{
    //    public Task<int> Execute();
    //}

    //public class ArchiveCommand : ICommand
    //{
    //    internal ArchiveCommand(ServiceProvider sp)
    //    {
    //        this.sp = sp;
    //    }

    //    private readonly ServiceProvider sp;

    //    internal ServiceProvider Services => sp;

    //    public async Task<int> Execute()
    //    {
    //        var ace = sp.GetRequiredService<ArchiveCommandExecutor>();

    //        return await ace.Execute();
    //    }
    //}

    //public class RestoreCommand : ICommand
    //{
    //    internal RestoreCommand(ServiceProvider sp)
    //    {
    //        this.sp = sp;
    //    }

    //    private readonly ServiceProvider sp;

    //    public ServiceProvider Services => sp;

    //    public async Task<int> Execute()
    //    {
    //        var ace = sp.GetRequiredService<RestoreCommandExecutor>();

    //        return await ace.Execute();
    //    }
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
}
