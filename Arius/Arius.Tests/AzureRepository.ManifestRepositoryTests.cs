﻿using Arius.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Tests
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
            var manifestRepo = TestSetup.GetServiceProvider().GetRequiredService<Repositories.AzureRepository.ManifestRepository>();

            Assert.CatchAsync<InvalidOperationException>(async () => await manifestRepo.GetChunkHashesAsync(new HashValue { Value = "idonotexist" }));
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