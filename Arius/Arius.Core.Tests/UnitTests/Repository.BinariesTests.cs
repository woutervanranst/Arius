using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MockQueryable.Moq;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests;

class BinaryRepositoryTests : TestBase
{
    [Test]
    public void GetChunkHashesAsync_InvalidManifestHash_InvalidOperationException()
    {
        var repo = GetRepository();

        Assert.CatchAsync<InvalidOperationException>(async () => await repo.Binaries.GetChunkHashesAsync(new BinaryHash("idonotexist")));
    }


    [Test]
    public async Task CreateChunkHashListAsync_BinaryWithOneChunk_Success()
    {
        var repo = GetRepository();

        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = new ChunkHash(bh).SingleToArray();

        await repo.Binaries.CreateChunkHashListAsync(bh, chs);

        // (implementation detail) no chunklist is created
        Assert.IsFalse(TestSetup.Container.GetBlobClient(repo.Binaries.GetChunkListBlobName(bh)).Exists());

        // Mock the backing db to have the BinaryProperties set 1 chunk
        repo.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Count()));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, repo.Binaries.GetChunkHashesAsync(bh).Result);
    }
    

    [Test]
    public async Task CreateChunkHashListAsync_BinaryWithMultipleChunk_Success()
    {
        var repo = GetRepository();

        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        await repo.Binaries.CreateChunkHashListAsync(bh, chs);

        // (implementation detail) no chunklist is created
        Assert.IsTrue(TestSetup.Container.GetBlobClient(repo.Binaries.GetChunkListBlobName(bh)).Exists());

        // Mock the backing db to have the BinaryProperties set 1 chunk
        repo.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Count()));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, repo.Binaries.GetChunkHashesAsync(bh).Result);
    }

    [Test]
    public async Task CreateChunkHashListAsync_CanOnlyBeCreatedOnce_Exception()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        var repo = GetRepository();

        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        await repo.Binaries.CreateChunkHashListAsync(bh, chs);

        Assert.CatchAsync<InvalidOperationException>(async () => await repo.Binaries.CreateChunkHashListAsync(bh, chs));
    }

    [Test]
    public async Task CreateChunkHashListAsync_RecreateInvalidZeroLength_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        const string tag = "Bla"; //intentionally capitalized to test case sensitivity

        var repo = GetRepository();

        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        // simulate an invalid Chunklist
        var ms = new MemoryStream();
        var bc = TestSetup.Container.GetBlobClient(repo.Binaries.GetChunkListBlobName(bh));
        bc.Upload(ms);
        throw new NotImplementedException();
        //await bc.SetMetadataTagAsync(tag);

        // create the chunkhashlist -- this will delete & recretate
        await repo.Binaries.CreateChunkHashListAsync(bh, chs);

        // the blob is replaced ( == the tag on the original blob will be gone)
        throw new NotImplementedException();
        //var hasTag = await bc.HasMetadataTagAsync(tag);
        //Assert.IsFalse(hasTag);

        // Mock the backing db to have the BinaryProperties set 1 chunk
        repo.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Count()));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, repo.Binaries.GetChunkHashesAsync(bh).Result);
    }

    [Test]
    public async Task CreateChunkHashListAsync_RecreateInvalidNoTag_Success()
    {
        var repo = GetRepository();

        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        // simulate an invalid Chunklist
        var ms = new MemoryStream(new byte[] { 1,2,3 });
        var bc = TestSetup.Container.GetBlobClient(repo.Binaries.GetChunkListBlobName(bh));
        bc.Upload(ms);

        // create the chunkhashlist -- this will delete & recretate
        await repo.Binaries.CreateChunkHashListAsync(bh, chs);

        // Mock the backing db to have the BinaryProperties set 1 chunk
        repo.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Count()));

        // we still get the correct chunkhash
        Assert.AreEqual(chs, repo.Binaries.GetChunkHashesAsync(bh).Result);
    }

    [Test]
    public async Task GetChunkHashesAsync_InvalidTag_Exception()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        var repo = GetRepository();

        var bh = new BinaryHash(Guid.NewGuid().ToString());
        var chs = Enumerable.Range(0, 1000).Select(_ => new ChunkHash(Guid.NewGuid().ToString())).ToArray();

        // create the chunkhashlist
        await repo.Binaries.CreateChunkHashListAsync(bh, chs);

        //remove the tag
        var bc = TestSetup.Container.GetBlobClient(repo.Binaries.GetChunkListBlobName(bh));
        await bc.SetMetadataAsync(default(IDictionary<string, string>));

        // Mock the backing db to have the BinaryProperties set 1 chunk
        repo.States.SetMockedDbContext(GetMockedContextWithBinaryProperty(bh, chs.Count()));

        // we get an exception
        Assert.CatchAsync<InvalidOperationException>(async () => await repo.Binaries.GetChunkHashesAsync(bh));
    }

    private static Repositories.Repository.AriusDbContext GetMockedContextWithBinaryProperty(BinaryHash bh, int chunkCount)
    {
        // ALTERNATIVE WITH IN MEMORY: https://stackoverflow.com/a/54220067/1582323


        var mockedBinaryPropertiesDbSet = new List<BinaryProperties>
        {
            new() { Hash = bh, ChunkCount = chunkCount }
        }.AsQueryable().BuildMockDbSet(); //https://github.com/romantitov/MockQueryable#how-do-i-get-started

        var mockContext = new Mock<Repositories.Repository.AriusDbContext>();
        mockContext
            .Setup(m => m.BinaryProperties)
            .Returns(mockedBinaryPropertiesDbSet.Object);

        return mockContext.Object;
    }
}