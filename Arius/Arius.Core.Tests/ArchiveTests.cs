using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Arius.Core.Tests
{
    class ArchiveTests : TestBase
    {
        protected override void BeforeEachTest()
        {
            ArchiveTestDirectory.Clear();
        }

        private AccessTier tier = AccessTier.Cool;


        private Lazy<FileInfo> sourceFileForArchiveNonDedup = new(() => 
        {
            var fn = Path.Combine(SourceFolder.FullName, "dir 1", "file 1.txt");
            var f = TestSetup.CreateRandomFile(fn, 0.5);

            return f;
        });
        private FileInfo EnsureFileForArchiveNonDedup(bool clearArchiveTestDirectory = false)
        {
            if (clearArchiveTestDirectory) 
                ArchiveTestDirectory.Clear();

            var s_fi = sourceFileForArchiveNonDedup.Value;
            var s_fi_rn = Path.GetRelativePath(SourceFolder.FullName, s_fi.FullName);
            var a_fi = new FileInfo(Path.Combine(ArchiveTestDirectory.FullName, s_fi_rn));
            a_fi.Directory.Create();

            if (!a_fi.Exists)
            { 
                s_fi.CopyTo(a_fi.FullName);
                a_fi.LastWriteTimeUtc = s_fi.LastWriteTimeUtc; //CopyTo does not do this somehow
            }

            return a_fi;
        }


        private void RepoStats(out Repository repo, 
            out int chunkBlobItemCount, 
            out int manifestCount, 
            out IEnumerable<PointerFileEntry> currentPfeWithDeleted, out IEnumerable<PointerFileEntry> currentPfeWithoutDeleted,
            out IEnumerable<PointerFileEntry> allPfes)
        {
            repo = GetRepository();

            chunkBlobItemCount = repo.GetAllChunkBlobs().Length;
            manifestCount = repo.GetManifestCount().Result;

            currentPfeWithDeleted = repo.GetCurrentEntries(true).Result.ToArray();
            currentPfeWithoutDeleted = repo.GetCurrentEntries(false).Result.ToArray();

            allPfes = repo.GetPointerFileEntries().Result.ToArray();
        }

        private void GetPointerInfo(FileInfo bfi, out PointerFile pf, out PointerFileEntry pfe) => GetPointerInfo(GetRepository(), bfi, out pf, out pfe);
        private void GetPointerInfo(Repository repo, FileInfo bfi, out PointerFile pf, out PointerFileEntry pfe)
        {
            var ps = GetPointerService();

            pf = ps.GetPointerFile(bfi);

            var a_rn = Path.GetRelativePath(ArchiveTestDirectory.FullName, bfi.FullName);
            pfe = repo.GetCurrentEntries(true).Result.SingleOrDefault(r => r.RelativeName.StartsWith(a_rn));
        }



        [Test, Order(1)]
        public async Task Archive_OneFile_Success()
        {
            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out _);

            var bfi = EnsureFileForArchiveNonDedup();
            AccessTier tier = AccessTier.Cool;
            await ArchiveCommand(tier);
            
            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out _);
            //1 additional chunk was uploaded
            Assert.AreEqual(chunkBlobItemCount0 + 1, chunkBlobItemCount1);
            //The chunk is in the appropriate tier
            Assert.AreEqual(tier, repo.GetAllChunkBlobs().First().AccessTier);
            //1 additional Manifest exists
            Assert.AreEqual(manifestCount0 + 1, manifestCount1);
            //1 additional PointerFileEntry exists
            Assert.AreEqual(currentPfeWithDeleted0.Count() + 1, currentPfeWithDeleted1.Count());
            Assert.AreEqual(currentPfeWithoutDeleted0.Count() + 1, currentPfeWithoutDeleted1.Count());

            GetPointerInfo(repo, bfi, out var pf, out var pfe);
            //PointerFile is created
            Assert.IsNotNull(pf);
            //There is a matching PointerFileEntry
            Assert.IsNotNull(pfe);
            //The PointerFileEntry is not marked as deleted
            Assert.IsFalse(pfe.IsDeleted);
            //The Creation- and LastWriteTime match
            Assert.AreEqual(bfi.CreationTimeUtc, pfe.CreationTimeUtc);
            Assert.AreEqual(bfi.LastWriteTimeUtc, pfe.LastWriteTimeUtc);
        }


        [Test]
        public async Task Archive_OneFileDeleteUndelete_Success()
        {
            var bfi = EnsureFileForArchiveNonDedup();
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
            _ = EnsureFileForArchiveNonDedup();
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
            var bfi1 = EnsureFileForArchiveNonDedup();
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
            var bfi = EnsureFileForArchiveNonDedup();
            await ArchiveCommand();


            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);

            GetPointerInfo(bfi, out var pf1, out _);

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
            var bfi1 = EnsureFileForArchiveNonDedup();
            await ArchiveCommand();


            //Rename BinaryFile + Pointer
            var pfi1 = bfi1.GetPointerFileInfoFromBinaryFile();
            var pfi1_FullName_Original = pfi1.FullName;
            bfi1.Rename($"Renamed {bfi1.Name}");
            pfi1.Rename($"Renamed {pfi1.Name}");

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
            Assert.AreEqual(currentPfeWithoutDeleted0.Count(), currentPfeWithoutDeleted0.Count());

            var pfi1_Relativename_Original = Path.GetRelativePath(ArchiveTestDirectory.FullName, pfi1_FullName_Original);
            var originalPfe = currentPfeWithDeleted1.SingleOrDefault(pfe => pfe.RelativeName == pfi1_Relativename_Original);
            // The original PointerFileEntry is marked as deleted
            Assert.IsTrue(originalPfe.IsDeleted);

            // No current entry exists for the original pointerfile
            originalPfe = currentPfeWithoutDeleted1.SingleOrDefault(pfe => pfe.RelativeName == pfi1_Relativename_Original);
            Assert.IsNull(originalPfe);

            var pfi1_Relativename_AfterMove = Path.GetRelativePath(TestSetup.ArchiveTestDirectory.FullName, pfi1.FullName);
            var movedPfe = currentPfeWithoutDeleted1.SingleOrDefault(lcf => lcf.RelativeName == pfi1_Relativename_AfterMove);
            // A new PointerFileEntry exists that is not marked as deleted
            Assert.IsFalse(movedPfe.IsDeleted);
        }





        [Test]
        public async Task Archive_RemoveLocal_Success()
        {
            var bfi = EnsureFileForArchiveNonDedup();
            // Ensure the BinaryFile exists
            Assert.IsTrue(File.Exists(bfi.FullName));

            await ArchiveCommand(removeLocal: true);
            // The BinaryFile no longer exists
            Assert.IsFalse(File.Exists(bfi.FullName));

            GetPointerInfo(bfi, out var pf, out var pfe);
            // But the PointerFile exists
            Assert.IsTrue(File.Exists(pf.FullName));
            // And the PointerFileEntry is not marked as deleted
            Assert.IsFalse(pfe.IsDeleted);
        }
    }
}
