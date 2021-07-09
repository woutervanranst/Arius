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
            out IEnumerable<PointerFileEntry> currentPfeWithDeleted, out IEnumerable<PointerFileEntry> currentPfeWithoutDeleted)
        {
            repo = GetRepository();

            chunkBlobItemCount = repo.GetAllChunkBlobs().Length;
            manifestCount = repo.GetManifestCount().Result;
            currentPfeWithDeleted = repo.GetCurrentEntries(true).Result.ToArray();
            currentPfeWithoutDeleted = repo.GetCurrentEntries(false).Result.ToArray();
        }

        private void GetPointerInfo(Repository repo, FileInfo bfi, out PointerFile pf, out PointerFileEntry pfe)
        {
            var ps = GetPointerService();

            pf = ps.GetPointerFile(bfi);

            var a_rn = Path.GetRelativePath(ArchiveTestDirectory.FullName, bfi.FullName);
            pfe = repo.GetCurrentEntries(true).Result.SingleOrDefault(r => r.RelativeName.StartsWith(a_rn));
        }


        [Test]
        public async Task Archive_OneFile_Success()
        {
            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0);


            var bfi = EnsureFileForArchiveNonDedup();
            
            AccessTier tier = AccessTier.Cool;
            await ArchiveCommand(tier);

            
            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1);

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
            // Ensure the file is there and archive it
            var bfi = EnsureFileForArchiveNonDedup();
            await ArchiveCommand();

            
            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0);


            // Delete the binary and the pointer
            ArchiveTestDirectory.Clear();

            //Archive again
            await ArchiveCommand();


            RepoStats(out var repo, out var chunkBlobItemCount1, out var manifestCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1);

            Assert.AreEqual(chunkBlobItemCount0, chunkBlobItemCount1);
            Assert.AreEqual(manifestCount0, manifestCount1);
            Assert.AreEqual(currentPfeWithDeleted0.Count(), currentPfeWithDeleted1.Count());
            Assert.AreEqual(currentPfeWithoutDeleted0.Count() - 1, currentPfeWithoutDeleted1.Count());


            GetPointerInfo(repo, bfi, out var pf, out var pfe);

            Assert.IsNull(pf);
            Assert.IsTrue(pfe.IsDeleted);



        }



    }
}
