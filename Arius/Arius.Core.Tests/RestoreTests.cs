using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Internal;

// https://www.automatetheplanet.com/nunit-cheat-sheet/

namespace Arius.Tests
{
    public partial class ArchiveRestoreTests
    {
        [OneTimeSetUp]
        public void ClassInit_Restore()
        {
            // Executes once for the test class. (Optional)

            //if (TestSetup.restoreTestDirectory.Exists) TestSetup.restoreTestDirectory.Delete(true);
            //TestSetup.restoreTestDirectory.Create();
        }

        [SetUp]
        public void TestInit_Restore()
        {
            // Runs before each test. (Optional)

            TestSetup.restoreTestDirectory.Clear();
        }

        private static readonly FileComparer comparer = new();

        [Test, Order(110)]
        public async Task Restore_OneFile_FromCoolTier()
        {
            Assert.IsTrue(TestSetup.restoreTestDirectory.IsEmpty());

            await RestoreCommand(synchronize: true, download: true, keepPointers: true);

            var archiveFiles = TestSetup.archiveTestDirectory.GetAllFiles();
            var restoredFiles = TestSetup.restoreTestDirectory.GetAllFiles();


            bool areIdentical = archiveFiles.SequenceEqual(restoredFiles, comparer);

            Assert.IsTrue(areIdentical);
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
            //SET UP -- Clear directories & create a new file (with new hash) to archive
            Assert.IsTrue(TestSetup.restoreTestDirectory.IsEmpty());
            TestSetup.archiveTestDirectory.Clear();
            var bfi = new FileInfo(Path.Combine(TestSetup.archiveTestDirectory.FullName, "archivefile2.txt"));
            Assert.IsFalse(bfi.Exists);
            TestSetup.CreateRandomFile(bfi.FullName, 0.1);


            //EXECUTE -- Archive to cool tier
            var services = await ArchiveCommand(AccessTier.Archive, dedup: false);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();


            //10 The chunk is in the Archive tier
            var pf = bfi.GetPointerFile();
            var chunkHashes = await repo.GetChunkHashesAsync(pf.Hash);
            var cb = repo.GetChunkBlobByHash(chunkHashes.Single(), false);
            Assert.AreEqual(AccessTier.Archive, cb.AccessTier);

            //11 A hydrated blob does not yet exist
            var bc_Hydrating = TestSetup.container.GetBlobClient($"{AzureRepository.ChunkRepository.RehydrationDirectoryName}/{cb.Name}");
            Assert.IsFalse(bc_Hydrating.Exists());
            
            //12 Obtaining properties results in an exception
            Assert.Catch<Azure.RequestFailedException>(() => bc_Hydrating.GetProperties());

            
            //EXECUTE -- Restore
            await RestoreCommand(synchronize: true, download: true, keepPointers: true);


            //ASSERT OUTCOME
            //20 A hydrating blob exists
            Assert.IsTrue(bc_Hydrating.Exists());

            //21 The status is rehydrate-pending
            var status = bc_Hydrating.GetProperties().Value.ArchiveStatus;
            Assert.IsTrue(status == "rehydrate-pending-to-cool" || status == "rehydrate-pending-to-hot");
        }


        [Test, Order(802)]
        public async Task Restore_FileArchiveTier_FromHydratedBlob()
        {
            //SET UP -- Clear directories & ceate a new file (with new hash) to archive
            Assert.IsTrue(TestSetup.restoreTestDirectory.IsEmpty());
            TestSetup.archiveTestDirectory.Clear();
            var bfi = new FileInfo(Path.Combine(TestSetup.archiveTestDirectory.FullName, "archivefile3.txt"));
            Assert.IsFalse(bfi.Exists);
            TestSetup.CreateRandomFile(bfi.FullName, 0.1);


            //EXECUTE -- Archive to cool tier
            var services = await ArchiveCommand(AccessTier.Cool, dedup: false);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();

            //10 - The chunk is in the cool tier
            var pf = bfi.GetPointerFile();
            var chunkHashes = await repo.GetChunkHashesAsync(pf.Hash);
            var cb = repo.GetChunkBlobByHash(chunkHashes.Single(), false);
            Assert.AreEqual(AccessTier.Cool, cb.AccessTier);


            //20 "Simulate" a hydrated blob
            //21 The original blob exists
            var bc_Original = TestSetup.container.GetBlobClient(cb.FullName);
            Assert.IsTrue(bc_Original.Exists());
            
            //22 The hydrated blob does not yet exist
            var bc_Hydrated = TestSetup.container.GetBlobClient($"{AzureRepository.ChunkRepository.RehydrationDirectoryName}/{cb.Name}");
            Assert.IsFalse(bc_Hydrated.Exists());
            
            //23 Copy the original to the hydrated folder
            var copyTask = bc_Hydrated.StartCopyFromUri(bc_Original.Uri);
            await copyTask.WaitForCompletionAsync();
            Assert.IsTrue(bc_Hydrated.Exists());
            
            //24 Move the original blob to the archive tier
            bc_Original.SetAccessTier(AccessTier.Archive);
            cb = repo.GetChunkBlobByHash(chunkHashes.Single(), false);
            Assert.AreEqual(AccessTier.Archive, cb.AccessTier);


            //EXECUTE -- Restore
            await RestoreCommand(synchronize: true, download: true, keepPointers: true);


            //ASSERT OUTCOME
            var archiveFiles = TestSetup.archiveTestDirectory.GetAllFiles();
            var restoredFiles = TestSetup.restoreTestDirectory.GetAllFiles();

            //30 The folders are identical
            bool areIdentical = archiveFiles.SequenceEqual(restoredFiles, comparer);
            Assert.IsTrue(areIdentical);
        }


        [Test, Order(1001)]
        public async Task Restore_FullSourceDirectory_NoPointers()
        {
            Assert.IsTrue(TestSetup.restoreTestDirectory.IsEmpty());

            await RestoreCommand(synchronize: true, download: true, keepPointers: false);

            var archiveFiles = TestSetup.archiveTestDirectory.GetAllFiles();
            var restoredFiles = TestSetup.restoreTestDirectory.GetAllFiles();


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
            Assert.IsTrue(TestSetup.restoreTestDirectory.IsEmpty());

            await RestoreCommand(synchronize: true, download: false, keepPointers: true);

            var archiveFiles = TestSetup.archiveTestDirectory.GetAllFiles();
            var restoredFiles = TestSetup.restoreTestDirectory.GetAllFiles();


            archiveFiles = archiveFiles.Where(fi => fi.IsPointerFile());

            bool areIdentical = archiveFiles.SequenceEqual(restoredFiles, comparer);

            Assert.IsTrue(areIdentical);
        }

        [Test, Order(1003)]
        public async Task Restore_FullSourceDirectory_Selectively()
        {
            Assert.IsTrue(TestSetup.restoreTestDirectory.IsEmpty());

            // Copy one pointer (to restore) to the restoredirectory
            var pfi1 = TestSetup.archiveTestDirectory.GetPointerFiles().First();
            pfi1 = pfi1.CopyTo(TestSetup.restoreTestDirectory);

            var pf1 = new PointerFile(TestSetup.restoreTestDirectory, pfi1);
            var bf1 = new BinaryFile(pf1.Root, pf1.BinaryFileInfo);

            Assert.IsTrue(File.Exists(pf1.FullName));
            Assert.IsFalse(File.Exists(bf1.FullName));


            //This is not yet implemented
            Assert.CatchAsync<ApplicationException>(async () => await RestoreCommand(synchronize: false, download: true, keepPointers: true));

            //var services = await RestoreCommand(synchronize: false, download: true, keepPointers: true);


            //Assert.IsTrue(File.Exists(pf1.FullName));
            //Assert.IsTrue(File.Exists(bf1.FullName));

            //IEnumerable<FileInfo> restoredFiles = TestSetup.restoreTestDirectory.GetAllFiles();

            ////Assert.IsTrue(pfi1.Exists);
            //Assert.IsNotNull(restoredFiles.Single(fi => fi.IsPointerFile()));
            //Assert.IsNotNull(restoredFiles.Single(fi => !fi.IsPointerFile()));
        }







        private async Task<IServiceProvider> RestoreCommand(bool synchronize, bool download, bool keepPointers)
        {
            var cmd = "restore " +
                $"-n {TestSetup.AccountName} " +
                $"-k {TestSetup.AccountKey} " +
                $"-p {TestSetup.passphrase} " +
                $"-c {TestSetup.container.Name} " +
                $"{(synchronize ? "--synchronize " : "")}" +
                $"{(download ? "--download " : "")}" +
                $"{(keepPointers ? "--keep-pointers " : "")}" +
                $"{TestSetup.restoreTestDirectory.FullName}";

            return await ExecuteCommand(cmd);
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

        //[TearDown]
        //public void TestCleanup()
        //{
        //    // Runs after each test. (Optional)
        //}

        //[OneTimeTearDown]
        //public void ClassCleanup()
        //{
        //    // Runs once after all tests in this class are executed. (Optional)
        //    // Not guaranteed that it executes instantly after all tests from the class.
        //}
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
}
