using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Tests;
using Arius.Core.Tests.Extensions;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Arius.Core.Tests.ApiTests;

class Restore_Tests_Old : TestBase
{
    protected override void BeforeEachTest()
    {
        TestSetup.RestoreTestDirectory.Clear();
    }

    private static readonly FileComparer comparer = new();

    [Test, Order(110)]
    public async Task Restore_OneFile_FromCoolTier()
    {
        //Assert.IsTrue(TestSetup.RestoreTestDirectory.IsEmpty());

        //await RestoreCommand(synchronize: true, download: true, keepPointers: true);

        //var archiveFiles = TestSetup.ArchiveTestDirectory.GetAllFileInfos();
        //var restoredFiles = TestSetup.RestoreTestDirectory.GetAllFileInfos();


        //bool areIdentical = archiveFiles.SequenceEqual(restoredFiles, comparer);

        //Assert.IsTrue(areIdentical);
    }


    /// <summary>
    /// Prepare a file
    /// 
    /// Expectation:
    /// 11/ a new file is stored in the cool tier
    /// 12/ hydration fails since it is already hydrated
    /// 
    /// </summary>
    /// <returns></returns>
    [Test, Order(801)]
    public async Task Restore_FileArchiveTier_HydratingBlob()
    {
        //if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
        //    return;

        ////SET UP -- Clear directories & create a new file (with new hash) to archive
        //Assert.IsTrue(TestSetup.RestoreTestDirectory.IsEmpty());
        //TestSetup.ArchiveTestDirectory.Clear();
        //var bfi = new FileInfo(Path.Combine(TestSetup.ArchiveTestDirectory.FullName, "archivefile2.txt"));
        //Assert.IsFalse(bfi.Exists);
        //TestSetup.CreateRandomFile(bfi.FullName, 100 * 1024);


        ////EXECUTE -- Archive to cool tier
        //var services = await ArchiveCommand(AccessTier.Archive, dedup: false);


        ////ASSERT OUTCOME
        //var repo = services.GetRequiredService<Repository>();


        ////10 The chunk is in the Archive tier
        //var ps = GetPointerService();
        //var pf = ps.GetPointerFile(bfi);
        //var chunkHashes = await repo.Binaries.GetChunkHashesAsync(pf.Hash);
        //var cb = repo.Chunks.GetChunkBlobByHash(chunkHashes.Single(), false);
        //Assert.AreEqual(AccessTier.Archive, cb.AccessTier);

        ////11 A hydrated blob does not yet exist
        //var bc_Hydrating = TestSetup.Container.GetBlobClient($"{Repository.ChunkRepository.RehydratedChunkFolderName}/{cb.Name}");
        //Assert.IsFalse(bc_Hydrating.Exists());

        ////12 Obtaining properties results in an exception
        //Assert.Catch<Azure.RequestFailedException>(() => bc_Hydrating.GetProperties());


        ////EXECUTE -- Restore
        //await RestoreCommand(synchronize: true, download: true, keepPointers: true);


        ////ASSERT OUTCOME
        ////20 A hydrating blob exists
        //Assert.IsTrue(bc_Hydrating.Exists());

        ////21 The status is rehydrate-pending
        //var status = bc_Hydrating.GetProperties().Value.ArchiveStatus;
        //Assert.IsTrue(status == "rehydrate-pending-to-cool" || status == "rehydrate-pending-to-hot");
    }


    [Test, Order(802)]
    public async Task Restore_FileArchiveTier_FromHydratedBlob()
    {
        //if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
        //    return;

        ////SET UP -- Clear directories & ceate a new file (with new hash) to archive
        //Assert.IsTrue(TestSetup.RestoreTestDirectory.IsEmpty());
        //TestSetup.ArchiveTestDirectory.Clear();
        //var bfi = new FileInfo(Path.Combine(TestSetup.ArchiveTestDirectory.FullName, "archivefile3.txt"));
        //Assert.IsFalse(bfi.Exists);
        //TestSetup.CreateRandomFile(bfi.FullName, 100 * 1024);


        ////EXECUTE -- Archive to cool tier
        //var services = await ArchiveCommand(AccessTier.Cool, dedup: false);


        ////ASSERT OUTCOME
        //var repo = services.GetRequiredService<Repository>();

        ////10 - The chunk is in the cool tier
        //var ps = GetPointerService();
        //var pf = ps.GetPointerFile(bfi);
        //var chunkHashes = await repo.Binaries.GetChunkHashesAsync(pf.Hash);
        //var cb = repo.Chunks.GetChunkBlobByHash(chunkHashes.Single(), false);
        //Assert.AreEqual(AccessTier.Cool, cb.AccessTier);


        ////20 "Simulate" a hydrated blob
        ////21 The original blob exists
        //var bc_Original = TestSetup.Container.GetBlobClient(cb.FullName);
        //Assert.IsTrue(bc_Original.Exists());

        ////22 The hydrated blob does not yet exist
        //var bc_Hydrated = TestSetup.Container.GetBlobClient($"{Repository.ChunkRepository.RehydratedChunkFolderName}/{cb.Name}");
        //Assert.IsFalse(bc_Hydrated.Exists());

        ////23 Copy the original to the hydrated folder
        //var copyTask = bc_Hydrated.StartCopyFromUri(bc_Original.Uri);
        //await copyTask.WaitForCompletionAsync();
        //Assert.IsTrue(bc_Hydrated.Exists());

        ////24 Move the original blob to the archive tier
        //bc_Original.SetAccessTier(AccessTier.Archive);
        //cb = repo.Chunks.GetChunkBlobByHash(chunkHashes.Single(), false);
        //Assert.AreEqual(AccessTier.Archive, cb.AccessTier);


        ////EXECUTE -- Restore
        //await RestoreCommand(synchronize: true, download: true, keepPointers: true);


        ////ASSERT OUTCOME
        //var archiveFiles = TestSetup.ArchiveTestDirectory.GetAllFileInfos();
        //var restoredFiles = TestSetup.RestoreTestDirectory.GetAllFileInfos();

        ////30 The folders are identical
        //bool areIdentical = archiveFiles.SequenceEqual(restoredFiles, comparer);
        //Assert.IsTrue(areIdentical);
    }


    [Test, Order(1001)]
    public async Task Restore_SynchronizeDirectoryNoPointers_Success()
    {
        Assert.IsTrue(TestSetup.RestoreTestDirectory.IsEmpty());

        await RestoreCommand(synchronize: true, download: true, keepPointers: false);

        var archiveFiles = TestSetup.ArchiveTestDirectory.GetAllFileInfos();
        var restoredFiles = TestSetup.RestoreTestDirectory.GetAllFileInfos();


        bool allNonPointerFilesAreRestored = !restoredFiles.Except(archiveFiles, comparer).Any();

        // all non pointer files are restored
        Assert.IsTrue(allNonPointerFilesAreRestored);

        // Does not contain pointer files
        var noPointerFiles = !restoredFiles.Any(fi => fi.IsPointerFile());
        Assert.IsTrue(noPointerFiles);
    }


    [Test, Order(1002)]
    public async Task Restore_FullSourceDirectory_OnlyPointers()
    {
        Assert.IsTrue(TestSetup.RestoreTestDirectory.IsEmpty());

        await RestoreCommand(synchronize: true, download: false, keepPointers: true);

        var archiveFiles = TestSetup.ArchiveTestDirectory.GetAllFileInfos();
        var restoredFiles = TestSetup.RestoreTestDirectory.GetAllFileInfos();


        archiveFiles = archiveFiles.Where(fi => fi.IsPointerFile());

        bool areIdentical = archiveFiles.SequenceEqual(restoredFiles, comparer);

        Assert.IsTrue(areIdentical);
    }









    private class FileComparer : IEqualityComparer<FileInfo>
    {
        public FileComparer() { }

        public bool Equals(FileInfo x, FileInfo y)
        {
            return x.Name == y.Name &&
                   x.Length == y.Length &&
                   x.LastWriteTimeUtc == y.LastWriteTimeUtc &&
                   SHA256Hasher.GetHashValue(x.FullName, "").Equals(SHA256Hasher.GetHashValue(y.FullName, ""));
        }

        public int GetHashCode(FileInfo obj)
        {
            return HashCode.Combine(obj.Name, obj.Length, obj.LastWriteTimeUtc, SHA256Hasher.GetHashValue(obj.FullName, ""));
        }
    }
}




//    /*
//     * Test cases
//     *      empty dir
//     *      dir with files > not to be touched?
//     *      dir with pointers - too many pointers > to be deleted
//     *      dir with pointers > not enough pointers > to be synchronzed
//     *      remote with isdeleted and local present > should be deleted
//     *      remote with !isdeleted and local not present > should be created
//     *      also in subdirectories
//     *      in ariusfile : de verschillende extensions
//     *      files met duplicates enz upload download
//     *      al 1 file lokaal > kopieert de rest
//     *      restore > normal binary file remains untouched
//     * directory more than 2 deep without other files
//     *  download > local files exist s> don't download all
// * restore naar directory waar al andere bestanden (binaries) instaan -< are not touched (dan moet ge maa rnaar ne lege restoren)

// restore a seoncd time without any changes
//     * */