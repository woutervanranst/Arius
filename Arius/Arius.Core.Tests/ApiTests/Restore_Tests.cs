using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Arius.Core.Tests;
using Arius.Core.Tests.Extensions;
using Azure.Storage.Blobs.Models;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.ApiTests;

class Restore_Tests : TestBase
{
    protected override void BeforeEachTest()
    {
        RestoreTestDirectory.Clear();
    }

    /*
     * Restore_SynchronizeDownloadFile              Restore_SynchronizeFile_ValidationException
     * Restore_SynchronizeDownloadDirectory
     * Restore_SynchronizeNoDownloadFile            Restore_SynchronizeFile_ValidationException
     * Restore_SynchroniseNoDownloadDirectory
     * Restore_NoSynchronizeDownloadFile
     * Restore_NoSynchronizeDownloadDirectory
     * Restore_NoSynchronizeNoDownloadFile
     * Restore_NoSynchronizeNoDownloadDirectory
     */

    [Test]
    public void Restore_SynchronizeFile_ValidationException([Values(true, false)] bool download) //Synchronize flag only valid on directory
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        var fn = Path.Combine(TestBase.RestoreTestDirectory.FullName, "ha.pointer.arius");
        File.WriteAllText(fn, "");

        Assert.CatchAsync<ValidationException>(async () => await RestoreCommand(synchronize: true, download: download, path: fn));

        File.Delete(fn);
    }

    [Test]
    public async Task Restore_SynchronizeDownloadDirectory()
    {
    }

    [Test]
    public async Task Restore_SynchroniseNoDownloadDirectory()
    {
    }

    [Test]
    public async Task Restore_NoSynchronizeDownloadFile()
    {
    }

    [Test]
    public async Task Restore_NoSynchronizeDownloadDirectory()
    {
    }

    [Test]
    public async Task Restore_NoSynchronizeNoDownloadFile()
    {
    }

    [Test]
    public async Task Restore_NoSynchronizeNoDownloadDirectory()
    {
    }
    





    /// <summary>
    /// Test the --synchronize flag
    /// </summary>
    /// <returns></returns>
    [Test] //Deze hoort bij Order(1001) maar gemaakt om apart te draaien
    public async Task Restore_SynchronizeNoDownloadDirectory_PointerFilesSynchronized()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        //Archive the full directory so that only pointers remain
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: true);

        var pf1 = ArchiveTestDirectory.GetPointerFileInfos().First();
        var pf2 = ArchiveTestDirectory.GetPointerFileInfos().Skip(1).First();
        var f3 = new FileInfo(Path.Combine(ArchiveTestDirectory.FullName, "randomotherfile.doc"));
        File.WriteAllText(f3.FullName, "stuff");

        //They do not refer to the same pointerfile
        Assert.IsTrue(pf1.FullName != pf2.FullName);

        pf1.Delete(); //we delete this one, it will be recreated
        pf2.Rename("bla.pointer.arius"); //we rename this one, it will be deleted


        //run archive
        await RestoreCommand(synchronize: true, download: false, keepPointers: true, path: TestSetup.ArchiveTestDirectory.FullName);

        Assert.IsTrue(pf1.Exists);
        Assert.IsFalse(pf2.Exists);
        Assert.IsTrue(f3.Exists); //non-pointer files remain intact
    }

    [Test]
    public async Task Restore_NoSynchronizeDownloadDirectory_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        // Selective restore

        //Archive the full directory so that only pointers remain
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: true);

        Assert.IsTrue(TestSetup.RestoreTestDirectory.IsEmpty());

        // Copy one pointer (to restore) to the restoredirectory
        var pfi1 = TestSetup.ArchiveTestDirectory.GetPointerFileInfos().First();
        pfi1 = pfi1.CopyTo(TestSetup.RestoreTestDirectory);

        //var pf1 = new PointerFile(TestSetup.RestoreTestDirectory, pfi1);
        //var bf1 = PointerService.GetBinaryFile(pf1); // new BinaryFile(pf1.Root, pf1.BinaryFileInfo);

        //Assert.IsTrue(File.Exists(pf1.FullName));
        //Assert.IsNull(bf1); //does not exist


        await RestoreCommand(synchronize: false, download: true, keepPointers: true);

        //var services = await RestoreCommand(synchronize: false, download: true, keepPointers: true);


        //Assert.IsTrue(File.Exists(pf1.FullName));
        //Assert.IsTrue(File.Exists(bf1.FullName));

        //IEnumerable<FileInfo> restoredFiles = TestSetup.restoreTestDirectory.GetAllFiles();

        ////Assert.IsTrue(pfi1.Exists);
        //Assert.IsNotNull(restoredFiles.Single(fi => fi.IsPointerFile()));
        //Assert.IsNotNull(restoredFiles.Single(fi => !fi.IsPointerFile()));

    }

    [Test]
    public async Task Restore_SynchronizeDownloadDirectory_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;
    }



    [Test]
    public async Task Restore_NoSynchronizeDownloadFile_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;
    }





    [Test]
    public async Task Restore_File_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        // Scenario: restore a single file

        //Archive the full directory so that only pointers remain
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: true);

        // 1. synchronize and do not download Directory: Restore_SynchronizeNoDownloadDirectory_PointerFilesSynchronized +  Restore_FullSourceDirectory_OnlyPointers
        // 2.1 synchronize and do not fownload file -- invalidoperaiton
        // 2.2 synchroniwe and download file -- invalidoperatoin
        // 3. do not synchronize and download file

        // synchronize and download Directory --> Restore_SynchronizeDirectoryNoPointers_Success
        // do not synchronize and download Directory --> Restore_NoSynchronizeDownload_Success



        // synchronize + file
        var pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var pfi2 = pfi.CopyTo(RestoreTestDirectory.CreateSubdirectory("subdir"));
        await RestoreCommand(synchronize: false, download: true, path: pfi2.FullName);

        var ps = GetPointerService();
        var pf = ps.GetPointerFile(pfi2);
        var bf = ps.GetBinaryFile(pf, true);

        Assert.NotNull(bf);
    }


    

    [Test]
    public async Task Restore_OneFileWithChunkAlreadyDownloaded_BinaryFileRestoredFromLocal()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        // Reset params
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal = false;
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier = false;
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration = false;


        // Ensure stuff is archived
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: false);

        // Copy the pointer and the chunk to the restore directory
        var a_pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var r_pfi = a_pfi.CopyTo(RestoreTestDirectory);
            
        var ps = GetPointerService();
        var a_pf = ps.GetPointerFile(a_pfi);
        var a_bf = ps.GetBinaryFile(a_pf, false);

        var restoreTempDir = GetRestoreTempDirectory(RestoreTestDirectory);

        var a_bfi = new FileInfo(a_bf.FullName);
        a_bfi.CopyTo(restoreTempDir, $"{a_bf.Hash}");
        // NOTE when fixing sep22: could not find ChunkFile anymore - this was the original line. a_bfi.CopyTo(restoreTempDir, $"{a_bf.Hash}{ChunkFile.Extension}");


        // Restore
        await RestoreCommand(RestoreTestDirectory.FullName, synchronize: false, download: true);


        // Assert
        // for the restore operation we only restored it from the local chunk cache
        //Assert.IsTrue(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal);
        //Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier);
        //Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration);

        // the BinaryFile is restored
        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
        Assert.IsNotNull(r_bfi);
    }

    [Test]
    public async Task Restore_OneFileWithChunkToDownload_BinaryFileRestored()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        // Reset params
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal = false;
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier = false;
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration = false;


        // Ensure stuff is archived
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: false);

        // Copy the pointer to the restore directory
        var a_pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var r_pfi = a_pfi.CopyTo(RestoreTestDirectory);


        // Restore
        await RestoreCommand(RestoreTestDirectory.FullName, synchronize: false, download: true);


        // Assert
        // for the restore operation we only restored it from the online tier
        //Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal);
        //Assert.IsTrue(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier);
        //Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration);

        // the BinaryFile is restored
        var ps = GetPointerService();
        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
        Assert.IsNotNull(r_bfi);
    }

    [Test]
    public async Task Restore_OneFileWithArchivedChunk_CannotYetBeRestored()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        // Reset params
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal = false;
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier = false;
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration = false;


        // Ensure stuff is archived
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(purgeRemote: true, removeLocal: false, tier: AccessTier.Archive);

        // Copy the pointer to the restore directory
        var a_pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var r_pfi = a_pfi.CopyTo(RestoreTestDirectory);


        // Restore
        await RestoreCommand(RestoreTestDirectory.FullName, synchronize: false, download: true);


        // Assert
        // for the restore operation we only started a hydration
        //Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal);
        //Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier);
        //Assert.IsTrue(Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration);

        // the BinaryFile is NOT restored
        var ps = GetPointerService();
        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
        Assert.IsNull(r_bfi);
    }

    [Test]
    public async Task Restore_OneFileWithTEMPHYDRATEDChunk_BinaryFileRestoredTempDeleted()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;
    }

    [Test]
    public async Task Restore_DedupedFile_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        EnsureArchiveTestDirectoryFileInfo();
        await ArchiveCommand(dedup: true);

        await RestoreCommand(RestoreTestDirectory.FullName, true, true, true);

        // the BinaryFile is restored
        var ps = GetPointerService();
        var r_pfi = RestoreTestDirectory.GetPointerFileInfos().Single();
        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
        Assert.IsNotNull(r_bfi);
    }

    [Test]
    public async Task Restore_DedupedDirectory_Success()
    {
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(purgeRemote: true, dedup: true, removeLocal: false);

        await RestoreCommand(RestoreTestDirectory.FullName, true, true);
    }
}