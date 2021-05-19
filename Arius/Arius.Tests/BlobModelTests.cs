using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Tests
{
    class BlobModelTests
    {
        [OneTimeSetUp]
        public void ClassInit_Archive()
        {
            // Executes once for the test class. (Optional)

            if (TestSetup.archiveTestDirectory.Exists) TestSetup.archiveTestDirectory.Delete(true);
            TestSetup.archiveTestDirectory.Create();
        }

        [SetUp]
        public void TestInit()
        {
            // Runs before each test. (Optional)
        }

        [Test]
        public void Properties_ChunkBlobBase_Equal()
        {
            var repo = TestSetup.GetAzureRepository();

            var cb1 = repo.GetAllChunkBlobs().First() as Models.ChunkBlobItem;
            var cb2 = repo.GetChunkBlobByHash(cb1.Hash, false) as Models.ChunkBlobClient;

            Assert.AreEqual(cb1.AccessTier, cb2.AccessTier);
            Assert.AreEqual(cb1.Downloadable, cb2.Downloadable);
            Assert.AreEqual(cb1.Folder, cb2.Folder);
            Assert.AreEqual(cb1.FullName, cb2.FullName);
            Assert.AreEqual(cb1.Hash, cb2.Hash);
            Assert.AreEqual(cb1.Length, cb2.Length);
            Assert.AreEqual(cb1.Name, cb2.Name);



            // * get byname that does not exist :e xpect null

            // *remove manifest blob length
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
