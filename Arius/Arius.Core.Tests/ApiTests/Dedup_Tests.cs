using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Tests.Extensions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Tests.ApiTests;

class Dedup_Tests : TestBase
{
    [Test]
    public async Task Archive_OneFile_Dedup_Success()
    {
        await TestSetup.PurgeRemote(); //purge the remote in case non-deduped files exist
        ArchiveTestDirectory.Clear();

        RepoStats(out var _, out var chunkBlobItemCount0, out var binaryCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);

        var bfi = EnsureArchiveTestDirectoryFileInfo();
        await ArchiveCommand(dedup: true);

        RepoStats(out var repo, out var chunkBlobItemCount1, out var binaryCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out var allPfes1);

        GetPointerInfo(repo, bfi, out var pf, out var pfe);
        var p = await repo.Binaries.GetPropertiesAsync(pf.Hash);

        Assert.IsTrue(p.ChunkCount > 1);
        Assert.AreEqual(chunkBlobItemCount0 + p.ChunkCount, chunkBlobItemCount1, p.ChunkCount.ToString());
        Assert.AreEqual(binaryCount0 + 1, binaryCount1);
    }


    [Test]
    public async Task Archive_Directory_Dedup_Success()
    {
        await TestSetup.PurgeRemote(); //purge the remote in case non-deduped files exist

        RepoStats(out var _, out var chunkBlobItemCount0, out var binaryCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);

        var bfis = EnsureArchiveTestDirectoryFileInfos();
        await ArchiveCommand(dedup: true);

        RepoStats(out var repo, out var chunkBlobItemCount1, out var binaryCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out var allPfes1);

        GetPointerInfo(repo, bfis[0], out var pf0, out var pfe0);
        var ch0 = await repo.Binaries.GetChunkHashesAsync(pf0.Hash);

        GetPointerInfo(repo, bfis[1], out var pf1, out var pfe1);
        var ch1 = await repo.Binaries.GetChunkHashesAsync(pf1.Hash);

        var bfi_deduped = bfis.Single(fi => fi.Name.Contains("deduplicated file"));
        GetPointerInfo(repo, bfi_deduped, out var pf5, out var pfe5);
        var ch5 = await repo.Binaries.GetChunkHashesAsync(pf5.Hash);
        var ch5_UniqueChunks = ch5.Except(ch0).Except(ch1).ToArray();

        // Validate that there are very few unique bytes
        var percentageUnique = (double)ch5_UniqueChunks.Length / (ch0.Length + ch1.Length);
        Assert.IsTrue(percentageUnique < 0.03, percentageUnique.ToString()); //0.03 value established empirically
        
        // There should only be one unique chunk (ok maybe two but this hasnt happened yet)
        Assert.AreEqual(1, ch5_UniqueChunks.Length);

        // The IncrementalLength is equal to the only unique chunk length
        var incrementalLength = (await repo.Binaries.GetPropertiesAsync(pf5.Hash)).IncrementalLength;
        var uniqueChunkSize = ch5_UniqueChunks.Select(ch => repo.Chunks.GetChunkBlobByHash(ch, false)).Sum(c => c.Length);
        Assert.AreEqual(incrementalLength, uniqueChunkSize);
    }
}