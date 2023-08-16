using Arius.Core.Commands;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests;

abstract class TestBase
{
    // https://www.automatetheplanet.com/nunit-cheat-sheet/

    [OneTimeSetUp]
    protected virtual void BeforeTestClass()
    {
        // Executes once for the test class. (Optional)
    }

    [SetUp]
    protected virtual async Task BeforeEachTest()
    {
        // Runs before each test. (Optional)
        BlockBase.Reset();

        // Ensure the Archive and Restore directories are empty
        ArchiveTestDirectory.Clear();
        RestoreTestDirectory.Clear();
    }

    [TearDown]
    protected virtual void AfterEachTest()
    {
        // Runs after each test. (Optional)
    }

    [OneTimeTearDown]
    protected virtual void AfterTestClass()
    {
        // Runs once after all tests in this class are executed. (Optional)
        // Not guaranteed that it executes instantly after all tests from the class.
    }

    
    protected Repository Repository => TestSetup.RepositoryFacade.Repository;

    /// <summary>
    /// Archive to the given tier
    /// </summary>
    protected static async Task ArchiveCommand(AccessTier tier = default, bool purgeRemote = false, bool removeLocal = false, bool fastHash = false, bool dedup = false)
    {
        if (tier == default)
            tier = AccessTier.Cool;

        if (purgeRemote)
            await TestSetup.PurgeRemote();

        await TestSetup.RepositoryFacade.ExecuteArchiveCommandAsync(TestSetup.ArchiveTestDirectory, fastHash, removeLocal, tier, dedup, DateTime.UtcNow);

        archiveHasRun = true;
    }

    protected static async Task EnsureArchiveCommandHasRun()
    {
        if (!archiveHasRun)
            await ArchiveCommand();
    }
    private static bool archiveHasRun = false;


    /// <summary>
    /// Restore to TestSetup.RestoreTestDirectory
    /// </summary>
    internal static async Task<IServiceProvider> RestoreCommand(bool synchronize, bool download, bool keepPointers)
    {
        return await RestoreCommand(
            synchronize: synchronize,
            download: download,
            keepPointers: keepPointers,
            path: TestSetup.RestoreTestDirectory.FullName);
    }

    /// <summary>
    /// Restore to the given path
    /// </summary>
    internal static async Task<IServiceProvider> RestoreCommand(string path, bool synchronize = false, bool download = false, bool keepPointers = true)
    {
        throw new NotImplementedException();

        //var c = TestSetup.Facade.CreateRestoreCommand(
        //    TestSetup.AccountName,
        //    TestSetup.AccountKey,
        //    TestSetup.Container.Name,
        //    TestSetup.Passphrase,
        //    synchronize,
        //    download,
        //    keepPointers,
        //    path,
        //    DateTime.UtcNow);

        //await c.Execute();

        //return c.Services;
    }


    //protected void RepoStats(out Repository repo,
    //    out int chunkBlobItemCount,
    //    out int binaryCount,
    //    out PointerFileEntry[] currentPfeWithDeleted, out PointerFileEntry[] currentPfeWithoutDeleted,
    //    out PointerFileEntry[] allPfes)
    //{
    //    repo = Repository;

    //    chunkBlobItemCount = Repository.Chunks.GetAllChunkBlobs().CountAsync().Result;
    //    binaryCount        = Repository.Binaries.CountAsync().Result;

    //    currentPfeWithDeleted    = Repository.PointerFileEntries.GetCurrentEntriesAsync(true).Result.ToArray();
    //    currentPfeWithoutDeleted = Repository.PointerFileEntries.GetCurrentEntriesAsync(false).Result.ToArray();

    //    throw new NotImplementedException();
    //    //allPfes = Repository.PointerFileEntries.GetPointerFileEntriesAsync().Result.ToArray();
    //}


    protected static DirectoryInfo SourceFolder         => TestSetup.SourceFolder;
    protected static DirectoryInfo ArchiveTestDirectory => TestSetup.ArchiveTestDirectory;
    protected static DirectoryInfo RestoreTestDirectory => TestSetup.RestoreTestDirectory;
}