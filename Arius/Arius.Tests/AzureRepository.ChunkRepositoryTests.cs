using Arius.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            var repo = TestSetup.GetServiceProvider().GetRequiredService<Repositories.AzureRepository.ChunkRepository>();
            
            var cb1 = repo.GetAllChunkBlobs().First();

            var cb2 = repo.GetChunkBlobByName(Repositories.AzureRepository.ChunkRepository.EncryptedChunkDirectoryName, cb1.Name);

            Assert.AreEqual(cb1.FullName, cb2.FullName);
        }


        public void GetChunkBlobByName_NotExisting_Null()
        {
            var repo = TestSetup.GetServiceProvider().GetRequiredService<Repositories.AzureRepository.ChunkRepository>();

            var cb = repo.GetChunkBlobByName(Repositories.AzureRepository.ChunkRepository.EncryptedChunkDirectoryName, "idonotexist");
            
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
