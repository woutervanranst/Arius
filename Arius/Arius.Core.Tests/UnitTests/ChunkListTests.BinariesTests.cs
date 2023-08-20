using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Repositories.BlobRepository;
using Azure.Storage.Blobs.Models;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests;

class ChunkListTests : TestBase
{
    [Test]
    public void GetChunkListAsync_InvalidManifestHash_InvalidOperationException()
    {
        Assert.CatchAsync<InvalidOperationException>(async () => await Repository.GetChunkListAsync(new BinaryHash("idonotexist".StringToBytes())).ToArrayAsync());
    }

    public static string GetChunkListBlobName(BinaryHash bh) => $"{BlobContainer.CHUNK_LISTS_FOLDER_NAME}/{bh.Value.BytesToHexString()}";

    [Test]
    public async Task CreateChunkListAsync_BinaryWithOneChunk_Success()
    {
        var bh = new BinaryHash(Guid.NewGuid().ToByteArray());
        var chs = ((ChunkHash)bh).AsArray();

        await Repository.CreateChunkListAsync(bh, chs);

        // (implementation detail) no chunklist is created
        Assert.IsFalse(await TestSetup.Container.GetBlobClient(GetChunkListBlobName(bh)).ExistsAsync());

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, await Repository.GetChunkListAsync(bh).ToArrayAsync());
    }

    [Test]
    public async Task CreateChunkListAsync_BinaryWithMultipleChunk_Success()
    {
        var bh = new BinaryHash(Guid.NewGuid().ToByteArray());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToByteArray())).ToArray();

        await Repository.CreateChunkListAsync(bh, chs);

        // (implementation detail) a chunklist is created
        Assert.IsTrue(await TestSetup.Container.GetBlobClient(GetChunkListBlobName(bh)).ExistsAsync());

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we still get the correct chunkhash

        Assert.AreEqual(chs, await Repository.GetChunkListAsync(bh).ToArrayAsync());
    }

    [Test]
    public async Task CreateChunkListAsync_AlreadyExists_Graceful()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        var bh = new BinaryHash(Guid.NewGuid().ToByteArray());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToByteArray())).ToArray();

        // Create the first time
        await Repository.CreateChunkListAsync(bh, chs);

        // Create the 2nd time // only a warning is logged, no exception is thrown
        await Repository.CreateChunkListAsync(bh, chs);
    }

    [Test]
    public async Task CreateChunkListAsync_RecreateInvalidZeroLength_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        var bh = new BinaryHash(Guid.NewGuid().ToByteArray());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToByteArray())).ToArray();

        // simulate an invalid Chunklist
        var ms = new MemoryStream();
        var bc = TestSetup.Container.GetBlobClient(GetChunkListBlobName(bh));
        bc.Upload(ms);
        var lmd = bc.GetProperties().Value.ETag;

        // create the chunkhashlist -- this will delete & recretate
        await Repository.CreateChunkListAsync(bh, chs);

        // the blob is replaced ( == the ETag is different)
        Assert.AreNotEqual(lmd, bc.GetProperties().Value.ETag);

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, await Repository.GetChunkListAsync(bh).ToArrayAsync());
    }

    [Test]
    public async Task CreateChunkListAsync_RecreateInvalidNoTag_Success()
    {
        var bh = new BinaryHash(Guid.NewGuid().ToByteArray());  
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToByteArray())).ToArray();

        // simulate an invalid Chunklist
        var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        var bc = TestSetup.Container.GetBlobClient(GetChunkListBlobName(bh));
        bc.Upload(ms);

        // create the chunkhashlist -- this will delete & recretate
        await Repository.CreateChunkListAsync(bh, chs);

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, await Repository.GetChunkListAsync(bh).ToArrayAsync());
    }

    [Test]
    public async Task GetChunkHashesAsync_InvalidTag_Exception()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        var bh = new BinaryHash(Guid.NewGuid().ToByteArray());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToByteArray())).ToArray();

        // create the chunkhashlist
        await Repository.CreateChunkListAsync(bh, chs);

        //remove the tag
        var bc = TestSetup.Container.GetBlobClient(GetChunkListBlobName(bh));
        await bc.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = "string" });

        // Mock the backing db to have the BinaryProperties set 1 chunk
        await CreateFakeBinaryPropertyAsync(bh, chs.Length);
        //Repository.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Length));

        // we get an exception
        Assert.CatchAsync<InvalidOperationException>(async () => await Repository.GetChunkListAsync(bh).ToArrayAsync());
    }

    private async Task CreateFakeBinaryPropertyAsync(BinaryHash bh, int chunkCount)
    {
        var f   = Path.GetTempFileName();
        var bfi = new BinaryFileInfo(f);
        var bf  = new BinaryFile(new DirectoryInfo(Path.GetTempPath()), bfi, bh);

        await Repository.CreateChunkEntryAsync(bf, 0, 0, chunkCount, null);
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