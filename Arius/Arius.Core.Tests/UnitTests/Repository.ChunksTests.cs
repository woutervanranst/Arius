using Arius.Core.Repositories;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Models;

namespace Arius.Core.Tests.UnitTests;

class ChunkRepositoryTests : TestBase
{
    [Test]
    public async Task GetChunkBlob_ExistingChunkBlob_ValidChunkBlob()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        TestSetup.StageArchiveTestDirectory(out FileInfo _);
        await EnsureArchiveCommandHasRun();

        var cb1 = await Repository.GetAllChunkBlobs().FirstAsync();

        var cb2 = await Repository.GetChunkBlobAsync(cb1.ChunkHash);

        Assert.AreEqual(cb1.FullName, cb2.FullName);
    }

    [Test]
    public async Task GetChunkBlob_NotExisting_NotNullNotExisting()
    {
        var cb = await Repository.GetChunkBlobAsync(new ChunkHash("idonotexist"));

        //Assert.IsNull(cb);
        Assert.IsNotNull(cb);
        Assert.IsFalse(cb.Exists);
    }
}