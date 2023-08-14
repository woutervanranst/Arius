using Arius.Core.Repositories;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests;

class ChunkRepositoryTests : TestBase
{
    [Test]
    public async Task GetChunkBlobByName_ExistingChunkBlob_ValidChunkBlob()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        TestSetup.StageArchiveTestDirectory(out FileInfo _);
        await EnsureArchiveCommandHasRun();

        var cb1 = await Repository.GetAllChunkBlobs().FirstAsync();

        var cb2 = Repository.GetChunkBlobByName(Repository.ChunksFolderName, cb1.Name);

        Assert.AreEqual(cb1.FullName, cb2.FullName);
    }

    [Test]
    public void GetChunkBlobByName_NotExisting_Null()
    {
        var cb = Repository.GetChunkBlobByName(Repository.ChunksFolderName, "idonotexist");

        Assert.IsNull(cb);
    }
}