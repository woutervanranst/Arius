using Arius.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Linq;

namespace Arius.Tests
{
    class ChunkRepositoryTests
    {
        [OneTimeSetUp]
        public void ClassInit_Archive()
        {
            // Executes once for the test class. (Optional)
        }

        [SetUp]
        public void TestInit()
        {
            // Runs before each test. (Optional)
        }


        [Test]
        public void GetChunkBlobByName_ExistingChunkBlob_ValidChunkBlob()
        {
            var repo = TestSetup.GetAzureRepository(); //TODO as ChunkRepository?
            
            var cb1 = repo.GetAllChunkBlobs().First();

            var cb2 = repo.GetChunkBlobByName(AzureRepository.ChunkRepository.EncryptedChunkDirectoryName, cb1.Name);

            Assert.AreEqual(cb1.FullName, cb2.FullName);
        }


        public void GetChunkBlobByName_NotExisting_Null()
        {
            var repo = TestSetup.GetAzureRepository(); //TODO as ChunkRepository?

            var cb = repo.GetChunkBlobByName(AzureRepository.ChunkRepository.EncryptedChunkDirectoryName, "idonotexist");
            
            Assert.IsNull(cb);
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
