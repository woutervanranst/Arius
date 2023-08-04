using Arius.Core.Commands;
using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.BehaviorTests;

[Binding]
internal static class TestSetup
{
    private const string TEST_CONTAINER_NAME_PREFIX = "behaviortest";

    private static BlobContainerClient container;

    [BeforeTestRun(Order = 1)]
    private static async Task ClassInit()
    {
        var options = GetRepositoryOptions();

        var blobService = options.GetBlobServiceClient();
        container = await blobService.CreateBlobContainerAsync(options.ContainerName);

        Facade = await new NewFacade(NullLoggerFactory.Instance)
            .ForStorageAccount(options)
            .ForRepositoryAsync(options);

        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<FileService>()
            .AddSingleton<IHashValueProvider, SHA256Hasher>()
            .BuildServiceProvider();

        FileService = sp.GetRequiredService<FileService>();


        await AddRepoStat();

        static RepositoryOptions GetRepositoryOptions()
        {
            var accountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
            if (string.IsNullOrEmpty(accountName))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_NAME not specified");

            var accountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");
            if (string.IsNullOrEmpty(accountKey))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_KEY not specified");

            var containerName = $"{TEST_CONTAINER_NAME_PREFIX}{DateTime.Now:yyMMdd-HHmmss}";

            var passphrase = "myPassphrase";

            return new RepositoryOptions(accountName, accountKey, containerName, passphrase);
        }
    }
        
    
    internal static RepositoryFacade Facade      { get; private set; }
    internal static FileService      FileService { get; private set; }
    
    internal static Repository Repository => Facade.Repository;


    [BeforeScenario]
    public static void ClearDirectories()
    {
        BlockBase.Reset();
    }

    [AfterTestRun]
    private static async Task ClassCleanup()
    {
        await PurgeRemoteAsync(false);
        Facade.Dispose();
    }
    

    // --------- REPOSTATS ---------

    private static async Task AddRepoStat()
    {
        var chunkCount = await Repository.Chunks.GetAllChunkBlobs().CountAsync();
        var binaryCount = await Repository.Binaries.CountAsync();

        //var currentWithDeletedPfes = (await repo.PointerFileEntries.GetCurrentEntriesAsync(true)).ToArray();
        //var currentExistingPfes = (await repo.PointerFileEntries.GetCurrentEntriesAsync(false)).ToArray();

        //var allPfes = (await repo.PointerFileEntries.GetPointerFileEntriesAsync()).ToArray();

        Stats.Add(new(chunkCount, binaryCount));
    }
    public record AriusRepositoryStats(int ChunkCount, int BinaryCount);
    public static List<AriusRepositoryStats> Stats { get; } = new();


    // --------- POINTERFILEENRY HELPERS ---------

    /// <summary>
    /// Get the PointerFileEntry associated with this PointerFile or BinaryFile
    /// </summary>
    /// <param name="relativeName">Either a PointerFile or a BinaryFile</param>
    /// <returns></returns>
    public static async Task<PointerFileEntry?> GetPointerFileEntryAsync(string relativeName)
    {
        var pfes = await Repository.PointerFileEntries.GetCurrentEntriesAsync(includeDeleted: true);
        var pfe  = pfes.SingleOrDefault(r => r.RelativeName.StartsWith(relativeName)); // StartsWith so relativeName can be both a PointerFile and a BinaryFile

        return pfe;
    }


    // --------- COMMANDS ---------

    public static async Task ArchiveCommandAsync(AccessTier tier, bool purgeRemote = false, bool removeLocal = false, bool fastHash = false, bool dedup = false)
    {
        if (purgeRemote)
            await PurgeRemoteAsync(true);

        await Facade.ExecuteArchiveCommandAsync(FileSystem.ArchiveDirectory, fastHash, removeLocal, tier, dedup, DateTime.UtcNow);

        await AddRepoStat();
    }


    public static async Task RestoreCommandAsyc(bool synchronize = false, bool download = false, bool keepPointers = true)
    {
        await RestoreCommandAsyc(FileSystem.RestoreDirectory, synchronize, download, keepPointers);
    }
    public static async Task RestoreCommandAsyc(DirectoryInfo path, bool synchronize = false, bool download = false, bool keepPointers = true)
    {
        await Facade.ExecuteRestoreCommandAsync(path, synchronize, download, keepPointers, DateTime.UtcNow);
    }


    // --------- BLOB HELPERS ---------

    private static async Task PurgeRemoteAsync(bool leaveContainer = false)
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
            foreach (var bci in blobService.GetBlobContainers(prefix: TestSetup.TEST_CONTAINER_NAME_PREFIX))
                await blobService.GetBlobContainerClient(bci.Name).DeleteAsync();
        }
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