using Arius.Core.Models;
using Arius.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests
{
    class BlobModelTests : TestBase
    {
        [OneTimeSetUp]
        public void ClassInit()
        {
            // Executes once for the test class. (Optional)

            if (TestSetup.ArchiveTestDirectory.Exists) TestSetup.ArchiveTestDirectory.Delete(true);
            TestSetup.ArchiveTestDirectory.Create();
        }

        [SetUp]
        public void TestInit()
        {
            // Runs before each test. (Optional)
        }

        [Test]
        public void Properties_ChunkBlobBase_Valid()
        {
            var repo = GetRepository();

            var cb1 = repo.GetAllChunkBlobs().First() as ChunkBlobItem;
            var cb2 = repo.GetChunkBlobByHash(cb1.Hash, false) as ChunkBlobClient;

            Assert.AreEqual(cb1.AccessTier, cb2.AccessTier);
            
            Assert.AreEqual(cb1.Downloadable, cb2.Downloadable);

            Assert.AreEqual(cb1.Folder, cb2.Folder);
            Assert.AreEqual(cb1.Folder, Repository.ChunkDirectoryName);
            
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
            var repo = GetRepository();

            //var manifestBlob = repo.GetAllManifestBlobs().First();

            var h = (await repo.GetAllManifestHashes()).First();
            var bi = TestSetup.Container.GetBlobs(prefix: $"{Repository.ManifestDirectoryName}/{h.Value}").Single();
            var manifestBlob = new ManifestBlob(bi);


            Assert.AreEqual(manifestBlob.Folder, Repository.ManifestDirectoryName);

            Assert.IsTrue(manifestBlob.FullName.Contains('/')); //the FullName contains the directory
            Assert.IsFalse(manifestBlob.FullName.Contains('.')); //the FullName does not have an extension

            Assert.NotNull(manifestBlob.Hash.Value);

            Assert.IsTrue(manifestBlob.Length > 0);

            Assert.IsFalse(manifestBlob.Name.Contains('/')); //the Name does NOT contain the directory
            Assert.IsFalse(manifestBlob.Name.Contains('.')); //the Name does not have an extension


            var mm = await repo.GetChunkHashesForManifestAsync(manifestBlob.Hash);
        }





        public void TestCleanup()
        {
            // Runs after each test. (Optional)
        }
        [OneTimeTearDown]
        public void ClassCleanup()
        {
            // Runs once after all tests in this class are executed. (Optional)
            // Not guaranteed that it executes instantly after all tests from the class.
        }
    }
}
