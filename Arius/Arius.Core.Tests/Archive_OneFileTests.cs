using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Arius.Core.Tests
{
    class Archive_OneFileTests : TestBase
    {
        protected override void BeforeEachTest()
        {
            ArchiveTestDirectory.Clear();
        }

        
        private readonly Lazy<FileInfo> sourceFile = new(() => 
        {
            var fn = Path.Combine(SourceFolder.FullName, nameof(Archive_OneFileTests), "file 1.txt");
            var f = TestSetup.CreateRandomFile(fn, 0.5);

            return f;
        });
        private FileInfo EnsureArchiveTestDirectoryFileInfo()
        {
            var sfi = sourceFile.Value;
            return sfi.CopyTo(SourceFolder, ArchiveTestDirectory);
        }


        [Test, Order(1)]
        public async Task Archive_OneFileCoolTier_Success()
        {
            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out _);

            var bfi = EnsureArchiveTestDirectoryFileInfo();
            AccessTier tier = AccessTier.Cool;
            await ArchiveCommand(tier);
            
            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out _);
            //1 additional chunk was uploaded
            Assert.AreEqual(chunkBlobItemCount0 + 1, chunkBlobItemCount1);
            //1 additional Manifest exists
            Assert.AreEqual(manifestCount0 + 1, manifestCount1);
            //1 additional PointerFileEntry exists
            Assert.AreEqual(currentPfeWithDeleted0.Count() + 1, currentPfeWithDeleted1.Count());
            Assert.AreEqual(currentPfeWithoutDeleted0.Count() + 1, currentPfeWithoutDeleted1.Count());

            GetPointerInfo(repo, bfi, out var pf, out var pfe);
            //PointerFile is created
            Assert.IsNotNull(pf);
            //The chunk is in the appropriate tier
            var ch = (await repo.GetChunkHashesForManifestAsync(pf.Hash)).Single();
            var c = repo.GetChunkBlobByHash(ch, requireHydrated: false);
            Assert.AreEqual(tier, c.AccessTier);
            //There is a matching PointerFileEntry
            Assert.IsNotNull(pfe);
            //The PointerFileEntry is not marked as deleted
            Assert.IsFalse(pfe.IsDeleted);
            //The Creation- and LastWriteTime match
            Assert.AreEqual(bfi.CreationTimeUtc, pfe.CreationTimeUtc);
            Assert.AreEqual(bfi.LastWriteTimeUtc, pfe.LastWriteTimeUtc);
        }


        [Test]
        public async Task Archive_DeleteUndelete_Success()
        {
            var bfi = EnsureArchiveTestDirectoryFileInfo();
            await ArchiveCommand();
            
            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);

            
            // DELETE
            // Delete the binary and the pointer
            ArchiveTestDirectory.Clear();
            await ArchiveCommand();

            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out var allPfes1);
            // The current archive is cleared
            Assert.AreEqual(currentPfeWithoutDeleted1.Count(), 0);
            // One additional PointerFileEntry marking it as deleted
            Assert.AreEqual(allPfes0.Count() + 1, allPfes1.Count());
            // Chunks have not changed
            Assert.AreEqual(chunkBlobItemCount0, chunkBlobItemCount1);
            // Manifests have not changed
            Assert.AreEqual(manifestCount0, manifestCount1);

            GetPointerInfo(repo, bfi, out var pf, out var pfe);
            Assert.IsNull(pf);
            Assert.IsTrue(pfe.IsDeleted);


            // UNDELETE
            _ = EnsureArchiveTestDirectoryFileInfo();
            await ArchiveCommand();

            RepoStats(out _, out var chunkBlobItemCount2, out var manifestCount2, out var currentPfeWithDeleted2, out var currentPfeWithoutDeleted2, out var allPfes2);
            // The current archive again has one file
            Assert.AreEqual(currentPfeWithoutDeleted2.Count(), 1);
            // One additinoal PointerFileEntry marking it as existing
            Assert.AreEqual(allPfes1.Count() + 1, allPfes2.Count());
            // Chunks have not changed
            Assert.AreEqual(chunkBlobItemCount0, chunkBlobItemCount2);
            // Manifests have not changed
            Assert.AreEqual(manifestCount0, manifestCount2);
        }


        [Test]
        public async Task Archive_DuplicateBinaryFile_Success()
        {
            var bfi1 = EnsureArchiveTestDirectoryFileInfo();
            await ArchiveCommand();

            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);

            // Add a duplicate of the BinaryFile
            var bfi2 = bfi1.CopyTo(ArchiveTestDirectory, $"Duplicate of {bfi1.Name}");
            // With slightly modified datetime
            bfi2.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            bfi2.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux

            await ArchiveCommand();


            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out var allPfes1);
            // No additional chunks were uploaded (ie just 1)
            Assert.AreEqual(chunkBlobItemCount1, 1);
            // No additional ManifestHash is created (ie just 1)
            Assert.AreEqual(manifestCount1, 1);
            // 1 addtl PointerFileEntry is created
            Assert.AreEqual(currentPfeWithoutDeleted0.Count() + 1, currentPfeWithoutDeleted1.Count());


            GetPointerInfo(repo, bfi2, out var pf2, out var pfe2);
            // A new PointerFile is created
            Assert.IsTrue(File.Exists(pf2.FullName));
            // A PointerFileEntry with the matching relativeName exists
            Assert.IsNotNull(pfe2);
            // The PointerFileEntry is not marked as deleted
            Assert.IsFalse(pfe2.IsDeleted);
            // The Creation- and LastWriteTimeUtc match
            Assert.AreEqual(bfi2.CreationTimeUtc, pfe2.CreationTimeUtc);
            Assert.AreEqual(bfi2.LastWriteTimeUtc, pfe2.LastWriteTimeUtc);
        }

        [Test]
        public async Task Archive_DuplicatePointerFile_Success()
        {
            var bfi1 = EnsureArchiveTestDirectoryFileInfo();
            await ArchiveCommand();


            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);

            GetPointerInfo(bfi1, out var pf1, out _);

            // Add a duplicate of the pointer
            var pfi2 = new FileInfo(Path.Combine(ArchiveTestDirectory.FullName, $"Duplicate of {pf1.Name}"));
            File.Copy(pf1.FullName, pfi2.FullName);
            // with slighty modified datetime
            pfi2.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            pfi2.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux

            await ArchiveCommand();


            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out var allPfes1);
            // No additional chunks were uploaded (ie just 1)
            Assert.AreEqual(chunkBlobItemCount0, chunkBlobItemCount1);
            // No additional ManifestHash is created (ie just 1)
            Assert.AreEqual(manifestCount0, manifestCount1);
            // 1 addtl PointerFileEntry is created
            Assert.AreEqual(currentPfeWithoutDeleted0.Count() + 1, currentPfeWithoutDeleted1.Count());


            GetPointerInfo(pfi2, out var pf2, out var pfe2);
            // Both PointerFiles still exist
            Assert.IsTrue(File.Exists(pf1.FullName));
            Assert.IsTrue(File.Exists(pf2.FullName));
            // A PointerFileEntry with the matching relativeName exists
            Assert.IsNotNull(pfe2);
            // The PointerFileEntry is not marked as deleted
            Assert.IsFalse(pfe2.IsDeleted);
            // The Creation- and LastWriteTimeUtc match
            Assert.AreEqual(pfi2.CreationTimeUtc, pfe2.CreationTimeUtc);
            Assert.AreEqual(pfi2.LastWriteTimeUtc, pfe2.LastWriteTimeUtc);
        }


        [Test]
        public async Task Archive_RenameBinaryFileWithPointerFile_Success()
        {
            // Rename BinaryFile and PointerFile -- this is like a 'move'

            var bfi = EnsureArchiveTestDirectoryFileInfo();
            await ArchiveCommand();


            //Rename BinaryFile + PointerFile
            var pfi = bfi.GetPointerFileInfoFromBinaryFile();
            var pfi_FullName_Original = pfi.FullName;
            bfi.Rename($"Renamed {bfi.Name}");
            pfi.Rename($"Renamed {pfi.Name}");

            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);

            await ArchiveCommand();


            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out var allPfes1);
            // No additional chunks were uploaded (ie just 1)
            Assert.AreEqual(chunkBlobItemCount0, chunkBlobItemCount1);
            // No additional ManifestHash is created (ie just 1)
            Assert.AreEqual(manifestCount0, manifestCount1);
            // One additional PointerFileEntry (the deleted one)
            Assert.AreEqual(currentPfeWithDeleted0.Count() + 1, currentPfeWithDeleted1.Count());
            // No net increase in current PointerFileEntries
            Assert.AreEqual(currentPfeWithoutDeleted0.Count(), currentPfeWithoutDeleted1.Count());

            var pfi_Relativename_Original = Path.GetRelativePath(ArchiveTestDirectory.FullName, pfi_FullName_Original);
            var originalPfe = currentPfeWithDeleted1.SingleOrDefault(pfe => pfe.RelativeName == pfi_Relativename_Original);
            // The original PointerFileEntry is marked as deleted
            Assert.IsTrue(originalPfe.IsDeleted);

            // No current entry exists for the original pointerfile
            originalPfe = currentPfeWithoutDeleted1.SingleOrDefault(pfe => pfe.RelativeName == pfi_Relativename_Original);
            Assert.IsNull(originalPfe);

            var pfi_Relativename_AfterMove = Path.GetRelativePath(ArchiveTestDirectory.FullName, pfi.FullName);
            var movedPfe = currentPfeWithoutDeleted1.SingleOrDefault(lcf => lcf.RelativeName == pfi_Relativename_AfterMove);
            // A new PointerFileEntry exists that is not marked as deleted
            Assert.IsFalse(movedPfe.IsDeleted);
        }


        [Test]
        public async Task Archive_RenameBinaryFileOnly_Success()
        {
            // Rename BinaryFile without renaming the PointerFile -- this is like a 'duplicate'

            var bfi = EnsureArchiveTestDirectoryFileInfo();
            await ArchiveCommand();


            //Rename BinaryFile
            var pfi = bfi.GetPointerFileInfoFromBinaryFile();
            var pfi_FullName_Original = pfi.FullName;
            bfi.Rename($"Renamed2 {bfi.Name}");
            //pfi.Rename($"Renamed {pfi1.Name}"); // <-- dit doen we hier NIET vs de vorige

            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);

            await ArchiveCommand();


            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out var allPfes1);
            // No additional chunks were uploaded (ie just 1)
            Assert.AreEqual(chunkBlobItemCount0, chunkBlobItemCount1);
            // No additional ManifestHash is created (ie just 1)
            Assert.AreEqual(manifestCount0, manifestCount1);
            // One additional PointerFileEntry (the deleted one)
            Assert.AreEqual(currentPfeWithDeleted0.Count() + 1, currentPfeWithDeleted1.Count());
            // One additional PointerFileEntry
            Assert.AreEqual(currentPfeWithoutDeleted0.Count() + 1, currentPfeWithoutDeleted1.Count()); //* CHANGED

            var pfi1_Relativename_Original = Path.GetRelativePath(ArchiveTestDirectory.FullName, pfi_FullName_Original);
            var originalPfe = currentPfeWithDeleted1.SingleOrDefault(pfe => pfe.RelativeName == pfi1_Relativename_Original);
            // The original PointerFileEntry is NOT marked as deleted
            Assert.IsFalse(originalPfe.IsDeleted); //* CHANGED

            // A current entry exists the original pointerfile and is not marked as deleted
            originalPfe = currentPfeWithoutDeleted1.SingleOrDefault(pfe => pfe.RelativeName == pfi1_Relativename_Original);
            Assert.IsFalse(originalPfe.IsDeleted); //* CHANED

            var pfi1_Relativename_AfterMove = Path.GetRelativePath(ArchiveTestDirectory.FullName, pfi.FullName);
            var movedPfe = currentPfeWithoutDeleted1.SingleOrDefault(lcf => lcf.RelativeName == pfi1_Relativename_AfterMove);
            // A new PointerFileEntry exists that is not marked as deleted
            Assert.IsFalse(movedPfe.IsDeleted);
        }


        [Test]
        public async Task Archive_RemoveLocal_Success()
        {
            var bfi = EnsureArchiveTestDirectoryFileInfo();
            // Ensure the BinaryFile exists
            Assert.IsTrue(File.Exists(bfi.FullName));

            await ArchiveCommand(removeLocal: true);
            // The BinaryFile no longer exists
            Assert.IsFalse(File.Exists(bfi.FullName));
            Assert.IsFalse(ArchiveTestDirectory.GetBinaryFileInfos().Any());

            GetPointerInfo(bfi, out var pf, out var pfe);
            // But the PointerFile exists
            Assert.IsTrue(File.Exists(pf.FullName));
            // And the PointerFileEntry is not marked as deleted
            Assert.IsFalse(pfe.IsDeleted);
        }


        [Test]
        public async Task Archive_RenamePointerFileWithoutBinaryFile_Success()
        {
            // Rename PointerFile that no longer has a BinaryFile -- this is like a 'move'
            
            var bfi = EnsureArchiveTestDirectoryFileInfo();
            await ArchiveCommand(removeLocal: true);
            
            
            Assert.IsFalse(File.Exists(bfi.FullName));

            //Rename PointerFile
            var pfi = bfi.GetPointerFileInfoFromBinaryFile();
            var pfi_FullName_Original = pfi.FullName;
            //bfi.Rename($"Renamed {bfi.Name}"); // <-- dit doen we hier NIET vs de vorige
            pfi.Rename($"Renamed3 {pfi.Name}"); 

            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);

            await ArchiveCommand();


            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out var allPfes1);
            // No additional chunks were uploaded (ie just 1)
            Assert.AreEqual(chunkBlobItemCount0, chunkBlobItemCount1);
            // No additional ManifestHash is created (ie just 1)
            Assert.AreEqual(manifestCount0, manifestCount1);
            // One additional PointerFileEntry (the deleted one)
            Assert.AreEqual(currentPfeWithDeleted0.Count() + 1, currentPfeWithDeleted1.Count());
            // No net increase in current PointerFileEntries
            Assert.AreEqual(currentPfeWithoutDeleted0.Count(), currentPfeWithoutDeleted1.Count());

            var pfi_Relativename_Original = Path.GetRelativePath(ArchiveTestDirectory.FullName, pfi_FullName_Original);
            var originalPfe = currentPfeWithDeleted1.SingleOrDefault(pfe => pfe.RelativeName == pfi_Relativename_Original);
            // The original PointerFileEntry is marked as deleted
            Assert.IsTrue(originalPfe.IsDeleted);

            // No current entry exists for the original pointerfile
            originalPfe = currentPfeWithoutDeleted1.SingleOrDefault(pfe => pfe.RelativeName == pfi_Relativename_Original);
            Assert.IsNull(originalPfe);

            var pfi_Relativename_AfterMove = Path.GetRelativePath(ArchiveTestDirectory.FullName, pfi.FullName);
            var movedPfe = currentPfeWithoutDeleted1.SingleOrDefault(lcf => lcf.RelativeName == pfi_Relativename_AfterMove);
            // A new PointerFileEntry exists that is not marked as deleted
            Assert.IsFalse(movedPfe.IsDeleted);
        }
    }
}
