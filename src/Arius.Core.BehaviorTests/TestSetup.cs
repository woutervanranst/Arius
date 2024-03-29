﻿using Arius.Core.Commands;
using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Repositories.BlobRepository;
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

        Facade = await new Facade.Facade(NullLoggerFactory.Instance)
            .ForStorageAccount(options)
            .ForRepositoryAsync(options);

        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<FileService>()
            .AddSingleton<IHashValueProvider>(new SHA256Hasher("somesalt"))
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

            var containerName = $"{TEST_CONTAINER_NAME_PREFIX}-{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}";

            var passphrase = "myPassphrase";

            return new RepositoryOptions(accountName, accountKey, containerName, passphrase);
        }
    }

    [AfterTestRun]
    private static async Task ClassCleanup()
    {
        await PurgeRemoteAsync(false);
        Facade.Dispose();
    }


    internal static RepositoryFacade Facade      { get; private set; }
    internal static FileService      FileService { get; private set; }
    
    internal static Repository Repository => Facade.Repository;


    [BeforeScenario]
    public static void ClearDirectories()
    {
        BlockBase.Reset();
    }
    

    // --------- REPOSTATS ---------

    private static async Task AddRepoStat()
    {
        var chunkEntryCount = await Repository.CountChunkEntriesAsync();
        var binaryCount     = await Repository.CountBinariesAsync();



        //var currentWithDeletedPfes = (await repo.PointerFileEntries.GetCurrentEntriesAsync(true)).ToArray();
        //var currentExistingPfes = (await repo.PointerFileEntries.GetCurrentEntriesAsync(false)).ToArray();

        //var allPfes = (await repo.PointerFileEntries.GetPointerFileEntriesAsync()).ToArray();
        var pfeCount = await Repository.CountPointerFileEntriesAsync();

        var clCount = await GetChunkListCount();

        Stats.Add(new(chunkEntryCount, binaryCount, pfeCount, clCount));
    }
    public record AriusRepositoryStats(int ChunkEntryCount, int BinaryCount, int PointerFileEntryCount, int ChunkListCount);
    public static List<AriusRepositoryStats> Stats { get; } = new();


    // --------- POINTERFILEENRY HELPERS ---------

    /// <summary>
    /// Get the PointerFileEntry associated with this PointerFile or BinaryFile
    /// </summary>
    /// <param name="relativeName">Either a PointerFile or a BinaryFile</param>
    /// <returns></returns>
    public static async Task<PointerFileEntry?> GetPointerFileEntryAsync(string relativeName)
    {
        var pfes = await Repository.GetCurrentPointerFileEntriesAsync(includeDeleted: true).ToArrayAsync();
        var pfe  = pfes.SingleOrDefault(r => r.RelativeName.StartsWith(relativeName)); // StartsWith so relativeName can be both a PointerFile and a BinaryFile

        return pfe;
    }


    // --------- COMMANDS ---------

    public static async Task<CommandResultStatus> ArchiveCommandAsync(string tier, bool purgeRemote = false, bool removeLocal = false, bool fastHash = false, bool dedup = false)
    {
        if (purgeRemote)
            await PurgeRemoteAsync(true);

        var (r, _) = await Facade.ExecuteArchiveCommandAsync(FileSystem.ArchiveDirectory, fastHash, removeLocal, tier, dedup, DateTime.UtcNow);

        await AddRepoStat();

        return r;
    }


    public static async Task<CommandResultStatus> RestoreCommandAsyc(bool synchronize = false, bool download = false, bool keepPointers = true)
    {
        return await RestoreCommandAsync(FileSystem.RestoreDirectory, synchronize, download, keepPointers);
    }
    public static async Task<CommandResultStatus> RestoreCommandAsync(DirectoryInfo path, bool synchronize = false, bool download = false, bool keepPointers = true)
    {
        return await Facade.ExecuteRestoreCommandAsync(path, synchronize, download, keepPointers, DateTime.UtcNow);
    }
    public static async Task<CommandResultStatus> RestoreCommandAsync(params string[] relativeNames)
    {
        return await Facade.ExecuteRestoreCommandAsync(FileSystem.RestoreDirectory, download: true, keepPointers: false, relativeNames: relativeNames);
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
            foreach (var bci in blobService.GetBlobContainers(prefix: $"{TEST_CONTAINER_NAME_PREFIX}-{DateTime.Now.AddHours(-1):yyMMddHHmmss}"))
                await blobService.GetBlobContainerClient(bci.Name).DeleteAsync();
        }
    }

    public static async Task<int> GetChunkListCount() => await container.GetBlobsAsync(prefix: $"{BlobContainer.CHUNK_LISTS_FOLDER_NAME}/").CountAsync();

    public static BlobClient GetBlobClient(string folder, ChunkHash h) => GetBlobClient(folder, h.Value);
    public static BlobClient GetBlobClient(string folder, byte[] hash) => GetBlobClient(folder, hash.BytesToHexString());
    public static BlobClient GetBlobClient(string folder, string name) => container.GetBlobClient($"{folder}/{name}");

    public static async Task<bool> RehydrateChunkExists(ChunkHash ch)
    {
        var c = GetBlobClient(BlobContainer.REHYDRATED_CHUNKS_FOLDER_NAME, ch);

        var p = await c.GetPropertiesAsync();
        var s = p.Value.ArchiveStatus;

        s.Should().BeOneOf("rehydrate-pending-to-cool", "rehydrate-pending-to-hot", "rehydrate-pending-to-cold");

        return await c.ExistsAsync();
    }

    public static async Task CopyChunkToRehydrateFolderAndArchiveOriginal(ChunkHash ch)
    {
        var source = GetBlobClient(BlobContainer.CHUNKS_FOLDER_NAME, ch);
        var target = GetBlobClient(BlobContainer.REHYDRATED_CHUNKS_FOLDER_NAME, ch);

        var t = await target.StartCopyFromUriAsync(source.Uri);
        await t.WaitForCompletionAsync();

        await source.SetAccessTierAsync(AccessTier.Archive);
    }

    public static async Task<bool> RehydrateFolderExists()
    {
        var n = await container.GetBlobsAsync(prefix: BlobContainer.REHYDRATED_CHUNKS_FOLDER_NAME).CountAsync();
        return n > 0;
    }
}