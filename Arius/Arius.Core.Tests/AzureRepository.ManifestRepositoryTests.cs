using Arius.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;

namespace Arius.Core.Tests
{
    class ManifestRepositoryTests
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
        public void GetChunkHashesAsync_InvalidManifestHash_InvalidOperationException()
        {
            var manifestRepo = TestSetup.GetRepository(); //TODO as ManifestRepository?

            Assert.CatchAsync<InvalidOperationException>(async () => await manifestRepo.GetChunkHashesForManifestAsync(new ManifestHash { Value = "idonotexist" }));
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
