using System;
using System.Collections.Generic;
using System.IO;
using Arius.Core.Configuration;
using Arius.Repositories;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Arius.Tests
{
    [SetUpFixture]
    public class TestSetup
    {
        public static DirectoryInfo sourceFolder;
        public static DirectoryInfo archiveTestDirectory;
        public static DirectoryInfo restoreTestDirectory;
        
        private static BlobServiceClient bsc;
        public static BlobContainerClient container;

        private static CloudTableClient ctc;
        public static readonly string passphrase = "myPassphrase";

        private const string TestContainerNamePrefix = "unittest";

        public static string AccountName { get; set; }
        public static string AccountKey { get; set; }
        //public static Core.Facade.Facade Facade { get; set; }
        //internal static IServiceProvider GetServiceProvider() => Facade.TEST;
        //internal static Repositories.AzureRepository GetAzureRepository() => GetServiceProvider().GetRequiredService<Repositories.AzureRepository>();


        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            // Executes once before the test run. (Optional)

            sourceFolder = PopulateSourceDirectory();

            // Create temp folder
            var containerName = TestContainerNamePrefix + $"{DateTime.Now.Ticks}";
            archiveTestDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "ariusunittests", "archive" + containerName));
            restoreTestDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "ariusunittests", "restore" + containerName));

            //testDirectoryInfo.Create();

            // Create temp container
            AccountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
            if (string.IsNullOrEmpty(AccountName))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_NAME not specified");

            AccountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");
            if (string.IsNullOrEmpty(AccountKey))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_KEY not specified");


            // Create new blob container
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={AccountName};AccountKey={AccountKey};EndpointSuffix=core.windows.net";
            bsc = new BlobServiceClient(connectionString);
            container = bsc.CreateBlobContainer(containerName);


            // Create reference to the storage tables
            var csa = CloudStorageAccount.Parse(connectionString);
            ctc = csa.CreateCloudTableClient();
        }

        private static DirectoryInfo PopulateSourceDirectory()
        {
            var sourceDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), ".ariustestsourcedir"));
            if (sourceDirectory.Exists) sourceDirectory.Delete(true);
            sourceDirectory.Create();

            CreateRandomFile(Path.Combine(sourceDirectory.FullName, "fileA.1"), 0.5);
            CreateRandomFile(Path.Combine(sourceDirectory.FullName, "fileB.1"), 2);
            CreateRandomFile(Path.Combine(sourceDirectory.FullName, "file with space.txt"), 5);
            CreateRandomFile(Path.Combine(sourceDirectory.FullName, $"directory with spaces{Path.DirectorySeparatorChar}file with space.txt"), 5);

            return sourceDirectory;
        }

        public static void CreateRandomFile(string fileFullName, double sizeInMB)
        {
            var f = new FileInfo(fileFullName);
            if (!f.Directory.Exists)
                f.Directory.Create();

            byte[] data = new byte[8192];
            var rng = new Random();

            using (FileStream stream = File.OpenWrite(fileFullName))
            {
                for (int i = 0; i < sizeInMB * 128; i++)
                {
                    rng.NextBytes(data);
                    stream.Write(data, 0, data.Length);
                }
            }
        }

        internal static AzureRepository GetAzureRepository()
        {
            return GetServiceProvider().GetRequiredService<AzureRepository>();
        }

        internal static IServiceProvider GetServiceProvider(AccessTier? tier = null, bool removeLocal = false, bool fastHash = false, bool dedup = false)
        {
            var f = CreateFacade(tier, removeLocal, fastHash, dedup);

            return f.ServiceProvider;
        }

        internal static Core.Facade.Facade CreateFacade(AccessTier? tier, bool removeLocal = false, bool fastHash = false, bool dedup = false)
        {
            var loggerFactory = new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory();

            var azCopyAppSettings = Options.Create(new AzCopyAppSettings()
            {
                BatchSize = 256 * 1024 * 1024, //256 MB
                BatchCount = 128
            });
            var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
            {
                TempDirectoryName = ".ariustemp",
                RestoreTempDirectoryName = ".ariusrestore"
            });

            var o = new Core.Facade.Facade.Options
            {
                AccountName = TestSetup.AccountName,
                AccountKey = TestSetup.AccountKey,
                Passphrase = TestSetup.passphrase,
                FastHash = fastHash,
                Container = TestSetup.container.Name,
                RemoveLocal = removeLocal,
                Tier = tier ?? AccessTier.Cool, //TODO
                Dedup = dedup,
                Path = TestSetup.archiveTestDirectory.FullName
            };

            var f = new Core.Facade.Facade(loggerFactory,
                azCopyAppSettings,
                tempDirectoryAppSettings,
                o);

            return f;
        }

        

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            archiveTestDirectory.Delete(true);

            foreach (var c in bsc.GetBlobContainers(prefix: TestContainerNamePrefix))
                bsc.GetBlobContainerClient(c.Name).Delete();

            foreach (var t in ctc.ListTables(prefix: TestContainerNamePrefix))
                t.Delete();
        }
    }
}