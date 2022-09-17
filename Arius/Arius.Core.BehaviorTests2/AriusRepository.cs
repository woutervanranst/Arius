using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.Core.Configuration;
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

            var containerName = $"{TestContainerNamePrefix}{DateTime.Now:yyMMdd-HHmmss}";

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
            var blobService = new BlobServiceClient(connectionString);
            Container = await blobService.CreateBlobContainerAsync(containerName);

            options = new RepositoryOptions(accountName, accountKey, Container.Name, passphrase);
            serviceProvider = ExecutionServiceProvider<RepositoryOptions>.BuildServiceProvider(NullLoggerFactory.Instance, options).Services;

            //oc.RegisterFactoryAs<Repository>((oc) => oc.Resolve<IServiceProvider>().GetRequiredService<Repository>()).InstancePerDependency();
            //oc.RegisterFactoryAs<PointerService>((oc) => oc.Resolve<IServiceProvider>().GetRequiredService<PointerService>()).InstancePerDependency();
        }

        private static IServiceProvider serviceProvider;
        private static RepositoryOptions options;
        internal static BlobContainerClient Container { get; private set; }

        [AfterTestRun]
        private static async Task ClassCleanup() => await PurgeRemote(false);

        private static async Task PurgeRemote(bool leaveContainer = false)
        {
            if (leaveContainer)
            {
                // Delete all blobs in the container but leave the container
                await foreach (var bi in Container.GetBlobsAsync())
                    await Container.DeleteBlobAsync(bi.Name);
            }
            else
            {
                // Delete all containers
                var blobService = Container.GetParentBlobServiceClient();
                foreach (var bci in blobService.GetBlobContainers(prefix: TestContainerNamePrefix))
                    await blobService.GetBlobContainerClient(bci.Name).DeleteAsync();
            }

        }

        [BeforeScenario]
        public static void ClearDirectories()
        {
            BlockBase.Reset();
        }



        private static Lazy<Facade> facade = new(() =>
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());

            var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
            {
                TempDirectoryName = ".ariustemp",
                RestoreTempDirectoryName = ".ariusrestore"
            });

            return new Facade(loggerFactory, tempDirectoryAppSettings);
        });


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


            var options2 = new ArchiveCommandOptions
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

            await archiveCommand.ExecuteAsync(options2);
        }
    }
}