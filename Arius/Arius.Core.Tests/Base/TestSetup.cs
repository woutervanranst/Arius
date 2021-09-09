using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

        public static Facade.Facade Facade { get; set; }

        private static DirectoryInfo unitTestRoot;
        public static DirectoryInfo SourceFolder { get; private set; }
        public static DirectoryInfo ArchiveTestDirectory { get; private set; }
        public static DirectoryInfo RestoreTestDirectory { get; private set; }


        private static BlobServiceClient blobService;
        public static BlobContainerClient Container { get; private set; }
        private static CloudTableClient table;


        [OneTimeSetUp]
        public static void OneTimeSetup()
        {
            // Executes once before the test run. (Optional)

            var containerName = $"{TestContainerNamePrefix}{DateTime.Now:yyMMddHHmmss}";
            unitTestRoot = new DirectoryInfo(Path.Combine(Path.GetTempPath(), containerName));

            //Create and populate source directory
            SourceFolder = unitTestRoot.CreateSubdirectory("source");
            //SourceFolder.Clear();
            //Populate(SourceFolder);

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
            table = csa.CreateCloudTableClient();


            // Initialize Facade
            var loggerFactory = new NullLoggerFactory();

            var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
            {
                TempDirectoryName = ".ariustemp",
                RestoreTempDirectoryName = ".ariusrestore"
            });

            Facade = new Facade.Facade(loggerFactory, tempDirectoryAppSettings);
        }


        public static FileInfo CreateRandomFile(string fileFullName, double sizeInMB) => CreateRandomFile(fileFullName, sizeInMB * 1024 * 1024);
        public static FileInfo CreateRandomFile(string fileFullName, int sizeInBytes)
        {
            // shttps://stackoverflow.com/q/4432178/1582323

            var f = new FileInfo(fileFullName);
            if (!f.Directory.Exists)
                f.Directory.Create();

            byte[] data = new byte[sizeInBytes];
            var rng = new Random();
            rng.NextBytes(data);
            File.WriteAllBytes(fileFullName, data);

            //byte[] data = new byte[8192];
            //var rng = new Random();

            //using (FileStream stream = File.OpenWrite(fileFullName))
            //{
            //    for (int i = 0; i < sizeInMB * 128; i++)
            //    {
            //        rng.NextBytes(data);
            //        stream.Write(data, 0, data.Length);
            //    }
            //}

            return f;
        }

        [OneTimeTearDown]
        public static async Task OneTimeTearDown()
        {
            unitTestRoot.Delete(true);

            foreach (var c in blobService.GetBlobContainers(prefix: TestContainerNamePrefix))
                await blobService.GetBlobContainerClient(c.Name).DeleteAsync();

            foreach (var t in table.ListTables(prefix: TestContainerNamePrefix))
                await t.DeleteAsync();
        }

        public static async Task PurgeRemote()
        {
            foreach (var b in Container.GetBlobs())
                Container.DeleteBlob(b.Name);

            foreach (var t in table.ListTables(prefix: TestContainerNamePrefix))
                foreach (var item in t.CreateQuery<TableEntity>())
                    await t.ExecuteAsync(TableOperation.Delete(item));
        }
    }
}