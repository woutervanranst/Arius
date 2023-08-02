using Arius.Core.Models;
using Azure.Storage.Blobs.Models;
using MockQueryable.Moq;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Repositories;
using Arius.Core.Services;

namespace Arius.Core.Tests.UnitTests;

class BinaryRepositoryTests : TestBase
{
    [Test]
    public void GetChunkHashesAsync_InvalidManifestHash_InvalidOperationException()
    {
        Assert.CatchAsync<InvalidOperationException>(async () => await Repository.Binaries.GetChunkListAsync(new BinaryHash("idonotexist")));
    }


    [Test]
    public async Task CreateChunkHashListAsync_BinaryWithOneChunk_Success()
    {
        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = ((ChunkHash)bh).AsArray();

        await Repository.Binaries.CreateChunkListAsync(bh, chs);

        // (implementation detail) no chunklist is created
        Assert.IsFalse(TestSetup.Container.GetBlobClient(Repository.Binaries.GetChunkListBlobName(bh)).Exists());

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, Repository.Binaries.GetChunkListAsync(bh).Result);
    }


    [Test]
    public async Task CreateChunkHashListAsync_BinaryWithMultipleChunk_Success()
    {
        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        await Repository.Binaries.CreateChunkListAsync(bh, chs);

        // (implementation detail) no chunklist is created
        Assert.IsTrue(TestSetup.Container.GetBlobClient(Repository.Binaries.GetChunkListBlobName(bh)).Exists());

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, Repository.Binaries.GetChunkListAsync(bh).Result);
    }

    [Test]
    public async Task CreateChunkHashListAsync_AlreadyExists_Graceful()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        // Create the first time
        await Repository.Binaries.CreateChunkListAsync(bh, chs);

        // Create the 2nd time // only a warning is logged, no exception is thrown
        await Repository.Binaries.CreateChunkListAsync(bh, chs);
    }

    [Test]
    public async Task CreateChunkHashListAsync_RecreateInvalidZeroLength_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        // simulate an invalid Chunklist
        var ms = new MemoryStream();
        var bc = TestSetup.Container.GetBlobClient(Repository.Binaries.GetChunkListBlobName(bh));
        bc.Upload(ms);
        var lmd = bc.GetProperties().Value.ETag;

        // create the chunkhashlist -- this will delete & recretate
        await Repository.Binaries.CreateChunkListAsync(bh, chs);

        // the blob is replaced ( == the ETag is different)
        Assert.AreNotEqual(lmd, bc.GetProperties().Value.ETag);

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, Repository.Binaries.GetChunkListAsync(bh).Result);
    }

    [Test]
    public async Task CreateChunkHashListAsync_RecreateInvalidNoTag_Success()
    {
        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        // simulate an invalid Chunklist
        var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        var bc = TestSetup.Container.GetBlobClient(Repository.Binaries.GetChunkListBlobName(bh));
        bc.Upload(ms);

        // create the chunkhashlist -- this will delete & recretate
        await Repository.Binaries.CreateChunkListAsync(bh, chs);

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, Repository.Binaries.GetChunkListAsync(bh).Result);
    }

    [Test]
    public async Task GetChunkHashesAsync_InvalidTag_Exception()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        // create the chunkhashlist
        await Repository.Binaries.CreateChunkListAsync(bh, chs);

        //remove the tag
        var bc = TestSetup.Container.GetBlobClient(Repository.Binaries.GetChunkListBlobName(bh));
        await bc.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = "string" });

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we get an exception
        Assert.CatchAsync<InvalidOperationException>(async () => await Repository.Binaries.GetChunkListAsync(bh));
    }

    private async Task CreateFakeBinaryPropertyAsync(BinaryHash bh, int chunkCount)
    {
        var f   = Path.GetTempFileName();
        var bfi = new BinaryFileInfo(f);
        var bf  = new BinaryFile(new DirectoryInfo(Path.GetTempPath()), bfi, bh);

        await Repository.Binaries.CreatePropertiesAsync(bf, 0, 0, chunkCount);
    }

    //private static Repositories.Repository.AriusDbContext GetMockedContextWithBinaryProperty(BinaryHash bh, int chunkCount)
    //{
    //    // ALTERNATIVE WITH IN MEMORY: https://stackoverflow.com/a/54220067/1582323

    //    var mockedBinaryPropertiesDbSet = new List<BinaryProperties>
    //    {
    //        new()
    //        {
    //            Hash = bh, 
    //            ChunkCount = chunkCount
    //        }
    //    }.AsQueryable().BuildMockDbSet(); //https://github.com/romantitov/MockQueryable#how-do-i-get-started

    //    var mockContext = new Mock<Repositories.Repository.AriusDbContext>();
    //    mockContext
    //        .Setup(m => m.BinaryProperties)
    //        .Returns(mockedBinaryPropertiesDbSet.Object);

    //    return mockContext.Object;
    //}
}