﻿using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Commands;

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


    private static readonly Lazy<FileInfo[]> sourceFiles = new(() =>
    {
        var fis = new List<FileInfo>
        {
            TestSetup.CreateRandomFile(Path.Combine(SourceFolder.FullName, "dir 1", "file 1.txt"), 512000 + 1), //make it an odd size to test buffer edge cases
            TestSetup.CreateRandomFile(Path.Combine(SourceFolder.FullName, "dir 1", "file 2.doc"), 2 * 1024 * 1024),
            TestSetup.CreateRandomFile(Path.Combine(SourceFolder.FullName, "dir 1", "file 3 large.txt"), 10 * 1024 * 1024),
            TestSetup.CreateRandomFile(Path.Combine(SourceFolder.FullName, "dir 2", "file4 with space.txt"), 1 * 1024 * 1024),
        };

        var f = Path.Combine(SourceFolder.FullName, "dir 2", "deduplicated file.txt"); //special file created out of two other files
        var concatenatedBytes = File.ReadAllBytes(fis[0].FullName).Concat(File.ReadAllBytes(fis[1].FullName));
        File.WriteAllBytes(f, concatenatedBytes.ToArray());
        fis.Add(new FileInfo(f));

        return fis.ToArray();

    }, isThreadSafe: false); //isThreadSafe because otherwise the tests go into a race condition to obtain the files
    protected static FileInfo EnsureArchiveTestDirectoryFileInfo()
    {
        var sfi = sourceFiles.Value.First();
        return sfi.CopyTo(SourceFolder, ArchiveTestDirectory);
    }
    protected static FileInfo[] EnsureArchiveTestDirectoryFileInfos()
    {
        var sfis = sourceFiles.Value;
        return sfis.Select(sfi => sfi.CopyTo(SourceFolder, ArchiveTestDirectory)).ToArray();
    }


    protected ServiceProvider GetServices()
    {
        return TestSetup.Facade.GetServices(
            TestSetup.AccountName,
            TestSetup.AccountKey,
            TestSetup.Container.Name,
            TestSetup.Passphrase);
    }
    protected Repository GetRepository()
    {
        return GetServices().GetRequiredService<Repository>();
    }
    protected PointerService GetPointerService()
    {
        return GetServices().GetRequiredService<PointerService>();
    }



    /// <summary>
    /// Archive to the cool tier
    /// </summary>
    protected static async Task ArchiveCommand(bool removeLocal = false, bool fastHash = false, bool dedup = false)
    {
        await ArchiveCommand(AccessTier.Cool, removeLocal, fastHash, dedup);
    }

    /// <summary>
    /// Archive to the given tier
    /// </summary>
    protected static async Task<IServiceProvider> ArchiveCommand(AccessTier tier, bool removeLocal = false, bool fastHash = false, bool dedup = false)
    {
        var c = TestSetup.Facade.CreateArchiveCommand(
            TestSetup.AccountName,
            TestSetup.AccountKey,
            TestSetup.Passphrase,
            fastHash,
            TestSetup.Container.Name,
            removeLocal,
            tier.ToString(),
            dedup,
            TestSetup.ArchiveTestDirectory.FullName);

        await c.Execute();

        archiveHasRun = true;

        return c.Services;
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
        var c = TestSetup.Facade.CreateRestoreCommand(
            TestSetup.AccountName,
            TestSetup.AccountKey,
            TestSetup.Container.Name,
            TestSetup.Passphrase,
            synchronize,
            download,
            keepPointers,
            path);

        await c.Execute();

        return c.Services;
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

        currentPfeWithDeleted = repo.PointerFileEntries.GetCurrentEntries(true).Result.ToArray();
        currentPfeWithoutDeleted = repo.PointerFileEntries.GetCurrentEntries(false).Result.ToArray();

        allPfes = repo.PointerFileEntries.GetPointerFileEntriesAsync().Result.ToArray();
    }


    /// <summary>
    /// Get the PoiinterFile and the PointerFileEntry for the given FileInfo fi.
    /// FileInfo fi can either be a PointerFile or a BinaryFile
    /// </summary>
    protected void GetPointerInfo(FileInfo fi, out PointerFile pf, out PointerFileEntry pfe) => GetPointerInfo(GetRepository(), fi, out pf, out pfe);
    /// <summary>
    /// Get the PoiinterFile and the PointerFileEntry for the given FileInfo fi.
    /// FileInfo fi can either be a PointerFile or a BinaryFile
    /// </summary>
    protected void GetPointerInfo(Repository repo, FileInfo fi, out PointerFile pf, out PointerFileEntry pfe)
    {
        var ps = GetPointerService();

        pf = ps.GetPointerFile(fi);

        var a_rn = Path.GetRelativePath(ArchiveTestDirectory.FullName, fi.FullName);
        pfe = repo.PointerFileEntries.GetCurrentEntries(includeDeleted: true).Result.SingleOrDefault(r => r.RelativeName.StartsWith(a_rn));
    }


    protected static DirectoryInfo SourceFolder => TestSetup.SourceFolder;
    protected static DirectoryInfo ArchiveTestDirectory => TestSetup.ArchiveTestDirectory;
    protected static DirectoryInfo RestoreTestDirectory => TestSetup.RestoreTestDirectory;
}