﻿using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Tests;
using Arius.Core.Tests.Extensions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests
{
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

            var cb1 = repo.GetAllChunkBlobs().First() as ChunkBlobItem;
            var cb2 = repo.GetChunkBlobByHash(cb1.Hash, false) as ChunkBlobBaseClient;

            Assert.AreEqual(cb1.AccessTier, cb2.AccessTier);

            Assert.AreEqual(cb1.Downloadable, cb2.Downloadable);

            Assert.AreEqual(cb1.Folder, cb2.Folder);
            Assert.AreEqual(cb1.Folder, Repository.ChunkFolderName);

            Assert.AreEqual(cb1.FullName, cb2.FullName);
            Assert.IsTrue(cb1.FullName.Contains('/')); //the FullName contains the directory
            Assert.IsTrue(cb1.FullName.EndsWith(ChunkBlobBase.Extension)); //the FullName contains the extension

            Assert.AreEqual(cb1.Hash, cb2.Hash);
            Assert.IsFalse(cb1.Hash.Value.EndsWith(ChunkBlobBase.Extension)); //the Hash does NOT contain the extension

            Assert.AreEqual(cb1.Length, cb2.Length);
            Assert.IsTrue(cb1.Length > 0);

            Assert.AreEqual(cb1.Name, cb2.Name);
            Assert.IsFalse(cb1.Name.Contains('/')); //the Name does NOT contain the directory
            Assert.IsTrue(cb1.Name.EndsWith(ChunkBlobBase.Extension)); //the Name contains the extension
        }

        [Test]
        public async Task Properties_ManifestBlob_Valid()
        {
            EnsureArchiveTestDirectoryFileInfo();
            await EnsureArchiveCommandHasRun();

            var repo = GetRepository();

            //var manifestBlob = repo.GetAllManifestBlobs().First();

            var h = (await repo.GetAllBinaryHashesAsync()).First();
            var bi = TestSetup.Container.GetBlobs(prefix: $"{Repository.BinaryManifestFolderName}/{h.Value}").Single();
            var manifestBlob = new BinaryManifest(bi);


            Assert.AreEqual(manifestBlob.Folder, Repository.BinaryManifestFolderName);

            Assert.IsTrue(manifestBlob.FullName.Contains('/')); //the FullName contains the directory
            Assert.IsFalse(manifestBlob.FullName.Contains('.')); //the FullName does not have an extension

            Assert.NotNull(manifestBlob.Hash.Value);

            Assert.IsTrue(manifestBlob.Length > 0);

            Assert.IsFalse(manifestBlob.Name.Contains('/')); //the Name does NOT contain the directory
            Assert.IsFalse(manifestBlob.Name.Contains('.')); //the Name does not have an extension


            var mm = await repo.GetChunksForBinaryAsync(manifestBlob.Hash);
        }
    }
}
