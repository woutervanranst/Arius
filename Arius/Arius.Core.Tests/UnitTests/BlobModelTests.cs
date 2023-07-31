using Arius.Core.Models;
using Arius.Core.Repositories;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests;

class BlobModelTests : TestBase
{
    protected override void BeforeTestClass()
    {
        ArchiveTestDirectory.Clear();
    }

    [Test]
    public async Task Properties_ChunkBlobBase_Valid()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        TestSetup.StageArchiveTestDirectory(out FileInfo _);
        await EnsureArchiveCommandHasRun();

        var repo = GetRepository();

        var cb1 = await repo.Chunks.GetAllChunkBlobs().FirstAsync() as ChunkBlobItem;
        var cb2 = repo.Chunks.GetChunkBlobByHash(cb1.ChunkHash, false) as ChunkBlobBaseClient;

        Assert.AreEqual(cb1.AccessTier, cb2.AccessTier);

        Assert.AreEqual(cb1.Downloadable, cb2.Downloadable);

        Assert.AreEqual(cb1.Folder, cb2.Folder);
        Assert.AreEqual(cb1.Folder, Repository.ChunkRepository.ChunkFolderName);

        Assert.AreEqual(cb1.FullName, cb2.FullName);
        Assert.IsTrue(cb1.FullName.Contains('/')); //the FullName contains the directory

        Assert.AreEqual(cb1.ChunkHash, cb2.ChunkHash);

        Assert.AreEqual(cb1.Length, cb2.Length);
        Assert.IsTrue(cb1.Length > 0);

        Assert.AreEqual(cb1.Name, cb2.Name);
        Assert.IsFalse(cb1.Name.Contains('/')); //the Name does NOT contain the directory
    }
}