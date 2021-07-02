﻿using Arius.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Linq;

namespace Arius.Core.Tests
{
    class ChunkRepositoryTests
    {
        [OneTimeSetUp]
        public void ClassInit()
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
            var repo = TestSetup.GetRepository();
            
            var cb1 = repo.GetAllChunkBlobs().First();

            var cb2 = repo.GetChunkBlobByName(Repository.ChunkDirectoryName, cb1.Name);

            Assert.AreEqual(cb1.FullName, cb2.FullName);
        }


        public void GetChunkBlobByName_NotExisting_Null()
        {
            var repo = TestSetup.GetRepository();

            var cb = repo.GetChunkBlobByName(Repository.ChunkDirectoryName, "idonotexist");
            
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
