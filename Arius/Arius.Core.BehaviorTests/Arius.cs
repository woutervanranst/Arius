using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.Core.Commands.Restore;
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
using NUnit.Framework;
using System.ComponentModel;
using TechTalk.SpecFlow;

namespace Arius.Core.BehaviorTests
{
    [Binding]
    static class Arius
    {
        
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

            await AddRepoStat();
        }
        
        private const string TestContainerNamePrefix = "unittest";
        private record RepositoryOptions(string AccountName, string AccountKey, string Container, string Passphrase) : IRepositoryOptions;
        private static RepositoryOptions options;
        private static BlobContainerClient container;
        internal static string ContainerName { get; private set; }
        internal static IServiceProvider GetServiceProvider() => ExecutionServiceProvider<RepositoryOptions>.BuildServiceProvider(NullLoggerFactory.Instance, options).Services;
        internal static Repository GetRepository() => GetServiceProvider().GetRequiredService<Repository>();
        internal static Lazy<PointerService> PointerService = new(() => GetServiceProvider().GetRequiredService<PointerService>());
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
        public record AriusRepositoryStats(int ChunkCount, int BinaryCount);
        public static List<AriusRepositoryStats> Stats { get; } = new();


        /// <summary>
        /// Get the PointerFileEntry associated with this PointerFile or BinaryFile
        /// </summary>
        /// <param name="relativeName">Either a PointerFile or a BinaryFile</param>
        /// <returns></returns>
        public static async Task<PointerFileEntry?> GetPointerFileEntryAsync(string relativeName)
        {
            var pfes = await GetRepository().PointerFileEntries.GetCurrentEntriesAsync(includeDeleted: true);
            var pfe = pfes.SingleOrDefault(r => r.RelativeName.StartsWith(relativeName)); // StartsWith so relativeName can be both a PointerFile and a BinaryFile

            return pfe;
        }



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
                Path = FileSystem.ArchiveDirectory,
                RemoveLocal = removeLocal,
                Tier = tier,
                VersionUtc = DateTime.UtcNow
            };

            await archiveCommand.ExecuteAsync(aco);

            await AddRepoStat();
        }
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


        public static async Task RestoreCommandAsyc(bool synchronize = false, bool download = false, bool keepPointers = true)
        {
            await RestoreCommandAsyc(FileSystem.RestoreDirectory, synchronize, download, keepPointers);
        }
        public static async Task RestoreCommandAsyc(DirectoryInfo path, bool synchronize = false, bool download = false, bool keepPointers = true)
        {
            var sp = new ServiceCollection()
                .AddAriusCoreCommands()
                .AddLogging()
                .BuildServiceProvider();
            var restoreCommand = sp.GetRequiredService<ICommand<IRestoreCommandOptions>>();

            var rco = new RestoreCommandOptions
            {
                AccountName = options.AccountName,
                AccountKey = options.AccountKey,
                Container = options.Container,
                Passphrase = options.Passphrase,
                Download = download,
                KeepPointers = keepPointers,
                Path = path,
                PointInTimeUtc = DateTime.UtcNow,
                Synchronize = synchronize
            };

            await restoreCommand.ExecuteAsync(rco);
        }
        private record RestoreCommandOptions : IRestoreCommandOptions
        {
            public bool Synchronize { get; init; }
            public bool Download { get; init; }
            public bool KeepPointers { get; init; }
            public DateTime? PointInTimeUtc { get; init; }
            public DirectoryInfo Path { get; init; }
            public string AccountName { get; init; }
            public string AccountKey { get; init; }
            public string Container { get; init; }
            public string Passphrase { get; init; }
        }


        public static async Task<bool> RehydrateChunkExists(ChunkHash ch)
        {
            var c = container.GetBlobClient($"{Repository.ChunkRepository.RehydratedChunkFolderName}/{ch}");

            var p = await c.GetPropertiesAsync();
            var s = p.Value.ArchiveStatus;

            s.Should().BeOneOf("rehydrate-pending-to-cool", "rehydrate-pending-to-hot");

            return await c.ExistsAsync();
        }

        public static async Task CopyChunkToRehydrateFolderAndArchiveOriginal(ChunkHash ch)
        {
            var source = container.GetBlobClient($"{Repository.ChunkRepository.ChunkFolderName}/{ch}");
            var target = container.GetBlobClient($"{Repository.ChunkRepository.RehydratedChunkFolderName}/{ch}");

            var t = await target.StartCopyFromUriAsync(source.Uri);
            await t.WaitForCompletionAsync();

            await source.SetAccessTierAsync(AccessTier.Archive);
        }
        public static async Task<bool> RehydrateFolderExists()
        {
            var n = await container.GetBlobsAsync(prefix: Repository.ChunkRepository.RehydratedChunkFolderName).CountAsync();
            return n > 0;
        }

    }
}