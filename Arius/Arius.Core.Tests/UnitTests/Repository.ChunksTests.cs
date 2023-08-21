using Arius.Core.Repositories;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests;

class ChunkRepositoryTests : TestBase
{
    //[Test]
    //public async Task GetChunkBlob_ExistingChunkBlob_ValidChunkBlob()
    //{
    //    if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
    //        return;

    //    TestSetup.StageArchiveTestDirectory(out FileInfo _);
    //    await EnsureArchiveCommandHasRun();

    //    var cb1 = await Repository.GetAllChunkBlobs().FirstAsync();

    //    var cb2 = await Repository.GetChunkBlobAsync(cb1.ChunkHash);

    //    Assert.AreEqual(cb1.FullName, cb2.FullName);
    //}



    

    [Test]
    public async Task UploadChunkedBunaryfdfdfAsync_OK_AlreadyExists_GracefulHandled()
    {
        // the metaata is set
        // the chunkentry matches
        // the totals match
        // is the incremental length double counted -- once for the chunk and once for the binary?

        // the BinaryExists
        // the chunk does not

        throw new NotImplementedException();
    }

    [Test]
    public async Task UploadChunkAsync_OK_AlreadyExists_GracefulHandled()
    {
        // the metaata is set
        // the chunkentry matches

        throw new NotImplementedException();
    }

    [Test]
    public async Task UploadChunkAsync_AlreadyExists_GracefulHandled()
    {
        // TODO test the case where a is already uploaded but does not exist in the db - the part with the OriginalContentLength etc
        // look for // return bbc.Length; // TODO what if the chunkentry does not exist

        throw new NotImplementedException();
    }

    [Test]
    public async Task SetAllChunks()
    {
        // the tier of a chunkenetry of a chunked binary in the db is not set
        // archive tier things are not updated

        throw new NotImplementedException();

    }

    [Test]
    public async Task AnArchiveWithAllChunksInCoolAreMigratedToCold()
    {
        // the tier of a chunkenetry of a chunked binary in the db is not set
        // archive tier things are not updated

        throw new NotImplementedException();
    }
}