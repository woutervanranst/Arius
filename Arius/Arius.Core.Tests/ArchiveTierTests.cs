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
    class ArchiveTierTests : TestBase
    {
        protected override void BeforeEachTest()
        {
            //base.BeforeEachTest();
        }


        [Test]
        public async Task Archive_OneFileArchiveTier_Success()
        {
            RepoStats(out _, out var chunkBlobItemCount0, out var manifestCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out _);

            var bfi = EnsureArchiveTestDirectoryFileInfo();
            AccessTier tier = AccessTier.Archive;
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
            //There is no hydrated chunk
            c = repo.GetChunkBlobByHash(c.Hash, requireHydrated: true);
            Assert.IsNull(c);
            //There is a matching PointerFileEntry
            Assert.IsNotNull(pfe);
            //The PointerFileEntry is not marked as deleted
            Assert.IsFalse(pfe.IsDeleted);
            //The Creation- and LastWriteTime match
            Assert.AreEqual(bfi.CreationTimeUtc, pfe.CreationTimeUtc);
            Assert.AreEqual(bfi.LastWriteTimeUtc, pfe.LastWriteTimeUtc);
        }
    }
}
