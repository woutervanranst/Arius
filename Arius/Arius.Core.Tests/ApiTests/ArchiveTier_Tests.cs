using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Tests;
using Azure.Storage.Blobs.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Arius.Core.Tests.ApiTests;

class ArchiveTier_Tests : TestBase
{
    protected override void BeforeEachTest()
    {
        //base.BeforeEachTest();
    }


    [Test]
    public async Task Archive_OneFileArchiveTier_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        RepoStats(out _, out var chunkBlobItemCount0, out var binaryCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out _);

        TestSetup.StageArchiveTestDirectory(out FileInfo bfi, sizeInBytes: 1024 * 1024 + 1); // Note: the file needs to be big enough (> 1 MB) to put into Archive storage (see ChunkBlobBase.SetAccessTierPerPolicyAsync) 
        AccessTier tier = AccessTier.Archive;
        await ArchiveCommand(tier);

        RepoStats(out var repo, out var chunkBlobItemCount1, out var binaryCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out _);
        //1 additional chunk was uploaded
        Assert.AreEqual(chunkBlobItemCount0 + 1, chunkBlobItemCount1);
        //1 additional Manifest exists
        Assert.AreEqual(binaryCount0 + 1, binaryCount1);
        //1 additional PointerFileEntry exists
        Assert.AreEqual(currentPfeWithoutDeleted0.Count() + 1, currentPfeWithoutDeleted1.Count());

        GetPointerInfo(repo, bfi, out var pf, out var pfe);
        //PointerFile is created
        Assert.IsNotNull(pf);
        //The chunk is in the appropriate tier
        var ch = (await repo.Binaries.GetChunkHashesAsync(pf.Hash)).Single();
        var c = repo.Chunks.GetChunkBlobByHash(ch, requireHydrated: false);
        Assert.AreEqual(tier, c.AccessTier);
        //There is no hydrated chunk
        c = repo.Chunks.GetChunkBlobByHash(c.Hash, requireHydrated: true);
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