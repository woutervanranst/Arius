using System;
using System.Collections.Generic;
using System.IO;
using Arius.Core.Configuration;
using Arius.Core.Repositories;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Arius.Core.Tests
{
    [SetUpFixture]
    static class TestSetup
    {
        public const string Passphrase = "myPassphrase";
        private const string TestContainerNamePrefix = "unittest";

        public static string AccountName { get; set; }
        public static string AccountKey { get; set; }
        
        public static Core.Facade.Facade Facade { get; set; }
        
        public static DirectoryInfo SourceFolder { get; private set; }
        public static DirectoryInfo ArchiveTestDirectory { get; private set; }
        public static DirectoryInfo RestoreTestDirectory { get; private set; }


        private static BlobServiceClient blobService;
        public static BlobContainerClient Container { get; private set; }
        private static CloudTableClient ctc;


        [OneTimeSetUp]
        public static void OneTimeSetup()
        {
            // Executes once before the test run. (Optional)

            var containerName = $"{TestContainerNamePrefix}{DateTime.Now.Ticks}";
            var unitTestRoot = new DirectoryInfo(Path.Combine(Path.GetTempPath(), containerName));

            //Create and populate source directory
            SourceFolder = unitTestRoot.CreateSubdirectory("source");
            SourceFolder.Clear();
            Populate(SourceFolder);

            ArchiveTestDirectory = unitTestRoot.CreateSubdirectory("archive");
            RestoreTestDirectory = unitTestRoot.CreateSubdirectory("restore");

            
            // Create temp container
            AccountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
            if (string.IsNullOrEmpty(AccountName))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_NAME not specified");

            AccountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");
            if (string.IsNullOrEmpty(AccountKey))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_KEY not specified");


            // Create new blob container
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={AccountName};AccountKey={AccountKey};EndpointSuffix=core.windows.net";
            blobService = new BlobServiceClient(connectionString);
            Container = blobService.CreateBlobContainer(containerName);


            // Create reference to the storage tables
            var csa = CloudStorageAccount.Parse(connectionString);
            ctc = csa.CreateCloudTableClient();


            // Initialize Facade
            var loggerFactory = new NullLoggerFactory();

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

            Facade = new Facade.Facade(loggerFactory, azCopyAppSettings, tempDirectoryAppSettings);
        }

        private static void Populate(DirectoryInfo dir)
        {
            CreateRandomFile(Path.Combine(dir.FullName, "fileA.1"), 0.5);
            CreateRandomFile(Path.Combine(dir.FullName, "fileB.1"), 2);
            CreateRandomFile(Path.Combine(dir.FullName, "file with space.txt"), 5);
            CreateRandomFile(Path.Combine(dir.FullName, $"directory with spaces{Path.DirectorySeparatorChar}file with space.txt"), 5);
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

        [OneTimeTearDown]
        public static void OneTimeTearDown()
        {
            foreach (var d in ArchiveTestDirectory.Parent.GetDirectories())
                d.Delete(true);

            foreach (var d in RestoreTestDirectory.Parent.GetDirectories())
                d.Delete(true);

            foreach (var c in blobService.GetBlobContainers(prefix: TestContainerNamePrefix))
                blobService.GetBlobContainerClient(c.Name).Delete();

            foreach (var t in ctc.ListTables(prefix: TestContainerNamePrefix))
                t.Delete();
        }
    }
}