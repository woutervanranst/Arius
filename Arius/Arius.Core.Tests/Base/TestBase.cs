using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Arius.Core.Facade;

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
    protected virtual void BeforeEachTest()
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



    private class ExecutionServicesRepositoryOptions : IRepositoryOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string ContainerName { get; init; }
        public string Passphrase { get; init; }
    }

    private IServiceProvider GetExecutionServices()
    {
        var options = new ExecutionServicesRepositoryOptions
        {
            AccountName = TestSetup.AccountName,
            AccountKey = TestSetup.AccountKey,
            ContainerName = TestSetup.Container.Name,
            Passphrase = TestSetup.Passphrase
        };

        var sp = ExecutionServiceProvider<ExecutionServicesRepositoryOptions>.BuildServiceProvider(NullLoggerFactory.Instance, options);

        return sp.Services;
    }

    protected Repository GetRepository() => GetExecutionServices().GetRequiredService<Repository>();
    protected FileService GetFileService() => GetExecutionServices().GetRequiredService<FileService>();
    protected IHashValueProvider GetHashValueProvider() => GetExecutionServices().GetRequiredService<IHashValueProvider>();
    protected ILogger<T> GetLogger<T>() => GetExecutionServices().GetRequiredService<ILogger<T>>();
    protected DirectoryInfo GetRestoreTempDirectory(DirectoryInfo root) => GetExecutionServices().GetRequiredService<Configuration.TempDirectoryAppSettings>().GetRestoreTempDirectory(root);
    



    private class ArchiveCommandOptions : IArchiveCommandOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string ContainerName { get; init; }
        public string Passphrase { get; init; }
        public bool FastHash { get; init; }
        public bool RemoveLocal { get; init; }
        public AccessTier Tier { get; init; }
        public bool Dedup { get; init; }
        public DirectoryInfo Path { get; init; }
        public DateTime VersionUtc { get; init; }
    }

    /// <summary>
    /// Archive to the given tier
    /// </summary>
    protected static async Task<IServiceProvider> ArchiveCommand(AccessTier tier = default, bool purgeRemote = false, bool removeLocal = false, bool fastHash = false, bool dedup = false)
    {
        if (tier == default)
            tier = AccessTier.Cool;

        if (purgeRemote)
            await TestSetup.PurgeRemote();

        var sp = new ServiceCollection()
            .AddAriusCoreCommands()
            .AddLogging()
            .BuildServiceProvider();
        var archiveCommand = sp.GetRequiredService<ICommand<IArchiveCommandOptions>>();


        var options = new ArchiveCommandOptions
        {
            AccountName = TestSetup.AccountName,
            AccountKey = TestSetup.AccountKey,
            ContainerName = TestSetup.Container.Name,
            Dedup = dedup,
            FastHash = fastHash,
            Passphrase = TestSetup.Passphrase,
            Path = TestSetup.ArchiveTestDirectory,
            RemoveLocal = removeLocal,
            Tier = tier,
            VersionUtc = DateTime.UtcNow
        };

        await archiveCommand.ExecuteAsync(options);

        archiveHasRun = true;

        return archiveCommand.Services;



        //var c = TestSetup.Facade.CreateArchiveCommand(
        //    TestSetup.AccountName,
        //    TestSetup.AccountKey,
        //    TestSetup.Passphrase,
        //    fastHash,
        //    TestSetup.Container.Name,
        //    removeLocal,
        //    tier.ToString(),
        //    dedup,
        //    TestSetup.ArchiveTestDirectory.FullName,
        //    DateTime.UtcNow);

        //await c.Execute();

        //archiveHasRun = true;

        //return c.Services;
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
    internal static async Task<IServiceProvider> RestoreCommand(
        string path,
        bool synchronize = false,
        bool download = false,
        bool keepPointers = true)
    {
        return null;

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


    protected void RepoStats(out Repository repo,
        out int chunkBlobItemCount,
        out int binaryCount,
        out PointerFileEntry[] currentPfeWithDeleted, out PointerFileEntry[] currentPfeWithoutDeleted,
        out PointerFileEntry[] allPfes)
    {
        repo = GetRepository();

        chunkBlobItemCount = repo.Chunks.GetAllChunkBlobs().CountAsync().Result;
        binaryCount = repo.Binaries.CountAsync().Result;

        currentPfeWithDeleted = repo.PointerFileEntries.GetCurrentEntriesAsync(true).Result.ToArray();
        currentPfeWithoutDeleted = repo.PointerFileEntries.GetCurrentEntriesAsync(false).Result.ToArray();

        allPfes = repo.PointerFileEntries.GetPointerFileEntriesAsync().Result.ToArray();
    }


    ///// <summary>
    ///// Get the PoiinterFile and the PointerFileEntry for the given FileInfo fi.
    ///// FileInfo fi can either be a PointerFile or a BinaryFile
    ///// </summary>
    //protected void GetPointerInfo(FileInfo fi, out PointerFile pf, out PointerFileEntry pfe) => GetPointerInfo(GetRepository(), fi, out pf, out pfe);
    ///// <summary>
    ///// Get the PointerFile and the PointerFileEntry for the given FileInfo fi.
    ///// FileInfo fi can either be a PointerFile or a BinaryFile
    ///// </summary>
    //protected void GetPointerInfo(Repository repo, FileInfo fi, out PointerFile pf, out PointerFileEntry pfe)
    //{
    //    var fs = GetFileService();

    //    pf = fs.GetExistingPointerFile(fi.Directory, FileSystemService.GetFileInfo(fi));

    //    var a_rn = Path.GetRelativePath(ArchiveTestDirectory.FullName, fi.FullName);
    //    pfe = repo.PointerFileEntries.GetCurrentEntriesAsync(includeDeleted: true).Result.SingleOrDefault(r => r.RelativeName.StartsWith(a_rn));
    //}


    protected static DirectoryInfo SourceFolder => TestSetup.SourceFolder;
    protected static DirectoryInfo ArchiveTestDirectory => TestSetup.ArchiveTestDirectory;
    protected static DirectoryInfo RestoreTestDirectory => TestSetup.RestoreTestDirectory;
}