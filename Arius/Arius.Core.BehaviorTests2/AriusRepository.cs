using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.Core.Configuration;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using TechTalk.SpecFlow;

namespace Arius.Core.BehaviorTests2
{
    [Binding]
    static class AriusRepository
    {
        private const string TestContainerNamePrefix = "unittest";
        private record RepositoryOptions(string AccountName, string AccountKey, string Container, string Passphrase) : IRepositoryOptions;

        [BeforeTestRun(Order = 1)]
        private static async Task ClassInit()
        {
            var accountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
            if (string.IsNullOrEmpty(accountName))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_NAME not specified");

            var accountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");
            if (string.IsNullOrEmpty(accountKey))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_KEY not specified");

            const string passphrase = "myPassphrase";

            ContainerName = $"{TestContainerNamePrefix}{DateTime.Now:yyMMdd-HHmmss}";

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
            var blobService = new BlobServiceClient(connectionString);
            container = await blobService.CreateBlobContainerAsync(ContainerName);

            options = new RepositoryOptions(accountName, accountKey, ContainerName, passphrase);

            //oc.RegisterFactoryAs<Repository>((oc) => oc.Resolve<IServiceProvider>().GetRequiredService<Repository>()).InstancePerDependency();
            //oc.RegisterFactoryAs<PointerService>((oc) => oc.Resolve<IServiceProvider>().GetRequiredService<PointerService>()).InstancePerDependency();

            await AddRepoStat();
        }

        private static RepositoryOptions options;
        private static BlobContainerClient container;
        internal static string ContainerName { get; private set; }
        internal static IServiceProvider GetServiceProvider() => ExecutionServiceProvider<RepositoryOptions>.BuildServiceProvider(NullLoggerFactory.Instance, options).Services;
        private static Repository GetRepository() => GetServiceProvider().GetRequiredService<Repository>();
        internal static Lazy<PointerService> PointerService = new(() => GetServiceProvider().GetRequiredService<PointerService>());


        [AfterTestRun]
        private static async Task ClassCleanup() => await PurgeRemote(false);

        private static async Task PurgeRemote(bool leaveContainer = false)
        {
            if (leaveContainer)
            {
                // Delete all blobs in the container but leave the container
                await foreach (var bi in container.GetBlobsAsync())
                    await container.DeleteBlobAsync(bi.Name);
            }
            else
            {
                // Delete all containers
                var blobService = container.GetParentBlobServiceClient();
                foreach (var bci in blobService.GetBlobContainers(prefix: TestContainerNamePrefix))
                    await blobService.GetBlobContainerClient(bci.Name).DeleteAsync();
            }

        }


        [BeforeScenario]
        public static void ClearDirectories()
        {
            BlockBase.Reset();
        }


        public record AriusRepositoryStats(int ChunkCount, int BinaryCount);
        public static List<AriusRepositoryStats> Stats { get; } = new();
        private static async Task AddRepoStat()
        {
            var repo = GetRepository();

            var chunkCount = await repo.Chunks.GetAllChunkBlobs().CountAsync();
            var binaryCount = await repo.Binaries.CountAsync();

            //var currentWithDeletedPfes = (await repo.PointerFileEntries.GetCurrentEntriesAsync(true)).ToArray();
            //var currentExistingPfes = (await repo.PointerFileEntries.GetCurrentEntriesAsync(false)).ToArray();

            //var allPfes = (await repo.PointerFileEntries.GetPointerFileEntriesAsync()).ToArray();

            Stats.Add(new(chunkCount, binaryCount));

        }


        //private static Lazy<Facade> facade = new(() =>
        //{
        //    var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());

        //    var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
        //    {
        //        TempDirectoryName = ".ariustemp",
        //        RestoreTempDirectoryName = ".ariusrestore"
        //    });

        //    return new Facade(loggerFactory, tempDirectoryAppSettings);
        //});


        private record ArchiveCommandOptions : IArchiveCommandOptions
        {
            public string AccountName { get; init; }
            public string AccountKey { get; init; }
            public string Container { get; init; }
            public string Passphrase { get; init; }
            public bool FastHash { get; init; }
            public bool RemoveLocal { get; init; }
            public AccessTier Tier { get; init; }
            public bool Dedup { get; init; }
            public DirectoryInfo Path { get; init; }
            public DateTime VersionUtc { get; init; }
        }


        //record ArchiveCommandOptions (string AccountName, string AccountKey, string Container, string Passphrase, bool FastHash, bool RemoveLocal, AccessTier Tier, bool Dedup, DirectoryInfo Path, DateTime VersionUtc) : IArchiveCommandOptions;

        public static async Task ArchiveCommandAsync(AccessTier tier, bool purgeRemote = false, bool removeLocal = false, bool fastHash = false, bool dedup = false)
        {
            if (purgeRemote)
                await PurgeRemote(true);

            var sp = new ServiceCollection()
                .AddAriusCoreCommands()
                .AddLogging()
                .BuildServiceProvider();
            var archiveCommand = sp.GetRequiredService<ICommand<IArchiveCommandOptions>>();

            var aco = new ArchiveCommandOptions
            {
                AccountName = options.AccountName,
                AccountKey = options.AccountKey,
                Container = options.Container,
                Dedup = dedup,
                FastHash = fastHash,
                Passphrase = options.Passphrase,
                Path = FileSystem.TestDirectory,
                RemoveLocal = removeLocal,
                Tier = tier,
                VersionUtc = DateTime.UtcNow
            };

            await archiveCommand.ExecuteAsync(aco);

            await AddRepoStat();
        }
    }
}