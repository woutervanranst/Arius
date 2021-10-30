using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Arius.Core.Tests;
using Arius.Core.Tests.Extensions;
using Azure.Storage.Blobs.Models;
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


    /// <summary>
    /// Test the --synchronize flag
    /// </summary>
    /// <returns></returns>
    [Test] //Deze hoort bij Order(1001) maar gemaakt om apart te draaien
    public async Task Restore_SynchronizeNoDownloadFolder_PointerFilesSynchronized()
    {
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
    public async Task Restore_SynchronizeFile_InvalidOperationException()
    {
        // Scenario: Restore - pass synchronize option and pass a single file as path -- should result in InvalidOperation

        //Archive the full directory so that only pointers remain
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: true);

        var pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var pfi2 = pfi.CopyTo(RestoreTestDirectory);
        Assert.CatchAsync<InvalidOperationException>(async () =>
        {
            try
            {
                await RestoreCommand(
                    synchronize: true,
                    download: false,
                    path: pfi2.FullName);
            }
            catch (AggregateException e)
            {
                throw e.InnerException.InnerException;
            }
        });
    }


    [Test]
    public async Task Restore_File_Success()
    {
        // Scenario: restore a single file

        //Archive the full directory so that only pointers remain
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: true);

        // 1. synchronize and do not download folder: Restore_SynchronizeNoDownloadFolder_PointerFilesSynchronized +  Restore_FullSourceDirectory_OnlyPointers
        // 2.1 synchronize and do not fownload file -- invalidoperaiton
        // 2.2 synchroniwe and download file -- invalidoperatoin
        // 3. do not synchronize and download file

        // synchronize and download folder --> Restore_SynchronizeDirectoryNoPointers_Success
        // do not synchronize and download folder --> Restore_NoSynchronizeDownload_Success



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
    public async Task Restore_NoSynchronizeDownload_Success()
    {
        // Selectively restore

        throw new NotImplementedException();

        //Assert.IsTrue(TestSetup.restoreTestDirectory.IsEmpty());

        //// Copy one pointer (to restore) to the restoredirectory
        //var pfi1 = TestSetup.archiveTestDirectory.GetPointerFileInfos().First();
        //pfi1 = pfi1.CopyTo(TestSetup.restoreTestDirectory);

        //var pf1 = new PointerFile(TestSetup.restoreTestDirectory, pfi1);
        //var bf1 = PointerService.GetBinaryFile(pf1); // new BinaryFile(pf1.Root, pf1.BinaryFileInfo);

        //Assert.IsTrue(File.Exists(pf1.FullName));
        //Assert.IsNull(bf1); //does not exist


        ////This is not yet implemented
        //Assert.CatchAsync<NotImplementedException>(async () => await RestoreCommand(synchronize: false, download: true, keepPointers: true));

        //var services = await RestoreCommand(synchronize: false, download: true, keepPointers: true);


        //Assert.IsTrue(File.Exists(pf1.FullName));
        //Assert.IsTrue(File.Exists(bf1.FullName));

        //IEnumerable<FileInfo> restoredFiles = TestSetup.restoreTestDirectory.GetAllFiles();

        ////Assert.IsTrue(pfi1.Exists);
        //Assert.IsNotNull(restoredFiles.Single(fi => fi.IsPointerFile()));
        //Assert.IsNotNull(restoredFiles.Single(fi => !fi.IsPointerFile()));
    }


    [Test]
    public async Task Restore_FileDoesNotExist_ValidationException()
    {
        //Archive the full directory so that only pointers remain
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: true);

        var pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var pfi2 = pfi.CopyTo(RestoreTestDirectory);

        // Delete the pointerfile
        pfi2.Delete();

        // Restore a file that does not exist
        Assert.CatchAsync<FluentValidation.ValidationException>(async () =>
            await RestoreCommand(path: pfi2.FullName));
    }

    [Test]
    public async Task Restore_FolderDoesNotExist_ValidationException()
    {
        throw new NotImplementedException();
    }

    [Test]
    public async Task Restore_OneFileWithChunkAlreadyDownloaded_BinaryFileRestoredFromLocal()
    {
        // Reset params
        Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal = false;
        Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier = false;
        Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration = false;


        // Ensure stuff is archived
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: false);

        // Copy the pointer and the chunk to the restore directory
        var a_pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var r_pfi = a_pfi.CopyTo(RestoreTestDirectory);
            
        var ps = GetServices().GetRequiredService<PointerService>();
        var a_pf = ps.GetPointerFile(a_pfi);
        var a_bf = ps.GetBinaryFile(a_pf, false);

        var restoreTempDir = GetServices().GetRequiredService<TempDirectoryAppSettings>().GetRestoreTempDirectory(RestoreTestDirectory);

        var a_bfi = new FileInfo(a_bf.FullName);
        a_bfi.CopyTo(restoreTempDir, $"{a_bf.Hash}{ChunkFile.Extension}");


        // Restore
        await RestoreCommand(RestoreTestDirectory.FullName, synchronize: false, download: true);


        // Assert
        // for the restore operation we only restored it from the local chunk cache
        Assert.IsTrue(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal);
        Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier);
        Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration);

        // the BinaryFile is restored
        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
        Assert.IsNotNull(r_bfi);
    }

    [Test]
    public async Task Restore_OneFileWithChunkToDownload_BinaryFileRestored()
    {
        // Reset params
        Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal = false;
        Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier = false;
        Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration = false;


        // Ensure stuff is archived
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(removeLocal: false);

        // Copy the pointer to the restore directory
        var a_pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var r_pfi = a_pfi.CopyTo(RestoreTestDirectory);


        // Restore
        await RestoreCommand(RestoreTestDirectory.FullName, synchronize: false, download: true);


        // Assert
        // for the restore operation we only restored it from the online tier
        Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal);
        Assert.IsTrue(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier);
        Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration);

        // the BinaryFile is restored
        var ps = GetServices().GetRequiredService<PointerService>();
        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
        Assert.IsNotNull(r_bfi);
    }

    [Test]
    public async Task Restore_OneFileWithArchivedChunk_CannotYetBeRestored()
    {
        // Reset params
        Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal = false;
        Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier = false;
        Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration = false;


        // Ensure stuff is archived
        await Archive_Directory_Tests.EnsureFullDirectoryArchived(purgeRemote: true, removeLocal: false, tier: AccessTier.Archive);

        // Copy the pointer to the restore directory
        var a_pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var r_pfi = a_pfi.CopyTo(RestoreTestDirectory);


        // Restore
        await RestoreCommand(RestoreTestDirectory.FullName, synchronize: false, download: true);


        // Assert
        // for the restore operation we only started a hydration
        Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal);
        Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier);
        Assert.IsTrue(Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration);

        // the BinaryFile is NOT restored
        var ps = GetServices().GetRequiredService<PointerService>();
        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
        Assert.IsNull(r_bfi);
    }

    [Test]
    public async Task Restore_OneFileWithTEMPHYDRATEDChunk_BinaryFileRestoredTempDeleted()
    {
        throw new NotImplementedException();
    }

    [Test]
    public async Task Restore_DedupedFile_Success()
    {
        EnsureArchiveTestDirectoryFileInfo();
        await ArchiveCommand(dedup: true);

        await RestoreCommand(RestoreTestDirectory.FullName, true, true, true);

        // the BinaryFile is restored
        var ps = GetServices().GetRequiredService<PointerService>();
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