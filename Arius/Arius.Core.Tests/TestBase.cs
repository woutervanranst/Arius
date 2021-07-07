﻿using Arius.Core.Extensions;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Core.Tests
{
    internal abstract class TestBase
    {
        [OneTimeSetUp]
        protected virtual void BeforeTestClass()
        {
            // Executes once for the test class. (Optional)
        }
        
        [SetUp]
        protected virtual void BeforeEachTest()
        {
            // Runs before each test. (Optional)
        }

        [TearDown]
        protected virtual void AfterEachTest()
        {
            // Runs after each test. (Optional)
        }
        
        [OneTimeTearDown]
        protected virtual void AfterTestClass()
        {
            // Runs once after all tests in this class are executed. (Optional)
            // Not guaranteed that it executes instantly after all tests from the class.
        }



        protected Repository GetRepository()
        {
            return GetServices().GetRequiredService<Repository>();
        }

        protected ServiceProvider GetServices()
        {
            return TestSetup.Facade.GetServices(
                TestSetup.AccountName,
                TestSetup.AccountKey,
                TestSetup.Container.Name,
                TestSetup.Passphrase);
        }

        protected async Task<IServiceProvider> ArchiveCommand(AccessTier tier, bool removeLocal = false, bool fastHash = false, bool dedup = false)
        {
            var c = TestSetup.Facade.CreateArchiveCommand(
                TestSetup.AccountName,
                TestSetup.AccountKey,
                TestSetup.Passphrase,
                fastHash,
                TestSetup.Container.Name,
                removeLocal,
                tier.ToString(),
                dedup,
                TestSetup.ArchiveTestDirectory.FullName);

            await c.Execute();

            return c.Services;
        }

        protected async Task<IServiceProvider> EnsureFullDirectoryArchived(bool removeLocal = false)
        {
            // Empty the test directory
            TestSetup.ArchiveTestDirectory.Clear();
            TestSetup.SourceFolder.CopyTo(TestSetup.ArchiveTestDirectory);

            //EXECUTE
            var services = await ArchiveCommand(AccessTier.Cool, removeLocal);
            return services;
        }


        /// <summary>
        /// Restore to TestSetup.RestoreTestDirectory
        /// </summary>
        internal async Task<IServiceProvider> RestoreCommand(bool synchronize, bool download, bool keepPointers)
        {
            return await RestoreCommand(
                synchronize: synchronize,
                download: download,
                keepPointers: keepPointers,
                path: TestSetup.RestoreTestDirectory.FullName);
        }

        /// <summary>
        /// Restore to the given path
        /// </summary>
        internal async Task<IServiceProvider> RestoreCommand(
            string path,
            bool synchronize = false,
            bool download = false,
            bool keepPointers = true)
        {
            var c = TestSetup.Facade.CreateRestoreCommand(
                TestSetup.AccountName,
                TestSetup.AccountKey,
                TestSetup.Container.Name,
                TestSetup.Passphrase,
                synchronize,
                download,
                keepPointers,
                path);

            await c.Execute();

            return c.Services;
        }

    }
}
