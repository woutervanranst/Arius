﻿using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Tests;
using Arius.Core.Tests.Extensions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
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
        EnsureArchiveTestDirectoryFileInfo();
        await EnsureArchiveCommandHasRun();

        var repo = GetRepository();

        var cb1 = repo.Chunks.GetAllChunkBlobs().First() as ChunkBlobItem;
        var cb2 = repo.Chunks.GetChunkBlobByHash(cb1.Hash, false) as ChunkBlobBaseClient;

        Assert.AreEqual(cb1.AccessTier, cb2.AccessTier);

        Assert.AreEqual(cb1.Downloadable, cb2.Downloadable);

        Assert.AreEqual(cb1.Folder, cb2.Folder);
        Assert.AreEqual(cb1.Folder, Repository.ChunkRepository.ChunkFolderName);

        Assert.AreEqual(cb1.FullName, cb2.FullName);
        Assert.IsTrue(cb1.FullName.Contains('/')); //the FullName contains the directory

        Assert.AreEqual(cb1.Hash, cb2.Hash);

        Assert.AreEqual(cb1.Length, cb2.Length);
        Assert.IsTrue(cb1.Length > 0);

        Assert.AreEqual(cb1.Name, cb2.Name);
        Assert.IsFalse(cb1.Name.Contains('/')); //the Name does NOT contain the directory
    }

    [Test]
    public async Task Properties_ManifestBlob_Valid()
    {
        EnsureArchiveTestDirectoryFileInfo();
        await EnsureArchiveCommandHasRun();

        var repo = GetRepository();

        //var manifestBlob = repo.GetAllManifestBlobs().First();

        var h = (await repo.Binaries.GetAllBinaryHashesAsync()).First();
        var bi = TestSetup.Container.GetBlobs(prefix: $"{Repository.BinaryRepository.ChunkListsFolderName}/{h.Value}").Single();
        var manifestBlob = new ChunkList(bi);


        Assert.AreEqual(manifestBlob.Folder, Repository.BinaryRepository.ChunkListsFolderName);

        Assert.IsTrue(manifestBlob.FullName.Contains('/')); //the FullName contains the directory
        Assert.IsFalse(manifestBlob.FullName.Contains('.')); //the FullName does not have an extension

        Assert.NotNull(manifestBlob.Hash.Value);

        Assert.IsTrue(manifestBlob.Length > 0);

        Assert.IsFalse(manifestBlob.Name.Contains('/')); //the Name does NOT contain the directory
        Assert.IsFalse(manifestBlob.Name.Contains('.')); //the Name does not have an extension


        var mm = await repo.Binaries.GetChunkHashesAsync(manifestBlob.Hash);
        throw new NotImplementedException(); // quid assertion
    }
}