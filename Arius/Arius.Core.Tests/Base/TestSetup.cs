using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arius.Core.Configuration;
using Arius.Core.Repositories;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Arius.Core.Tests;

[SetUpFixture]
internal static class TestSetup
{
    public static readonly DateTime UnitTestGracePeriod = new(2021, 11, 9);

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
    //private static TableServiceClient tableService;


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


        //// Create reference to the storage tables
        //tableService = new TableServiceClient(connectionString);


        // Initialize Facade
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());

        var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
        {
            TempDirectoryName = ".ariustemp",
            RestoreTempDirectoryName = ".ariusrestore"
        });

        Facade = new Facade.Facade(loggerFactory, tempDirectoryAppSettings);
    }


    public static FileInfo CreateRandomFile(string fileFullName, int sizeInBytes)
    {
        // https://stackoverflow.com/q/4432178/1582323

        var f = new FileInfo(fileFullName);
        if (!f.Directory.Exists)
            f.Directory.Create();

        byte[] data = new byte[sizeInBytes];
        var rng = new Random();
        rng.NextBytes(data);
        File.WriteAllBytes(fileFullName, data);

        return f;
    }

    [OneTimeTearDown]
    public static async Task OneTimeTearDown()
    {
        // Delete local temp
        foreach (var d in new DirectoryInfo(Path.GetTempPath()).GetDirectories($"{TestContainerNamePrefix}*"))
            d.Delete(true);

        // Delete blobs
        foreach (var bci in blobService.GetBlobContainers(prefix: TestContainerNamePrefix))
            await blobService.GetBlobContainerClient(bci.Name).DeleteAsync();

        //// Delete tables
        //foreach (var ti in tableService.Query())
        //    if (ti.Name.StartsWith(TestContainerNamePrefix))
        //        await tableService.GetTableClient(ti.Name).DeleteAsync();
    }

    public static async Task PurgeRemote()
    {
        // delete all blobs in the container but leave the container
        foreach (var bi in Container.GetBlobs())
            await Container.DeleteBlobAsync(bi.Name);

        //// delete all rows but leave the container
        //foreach (var ti in tableService.Query())
        //{
        //    if (ti.Name.StartsWith(TestContainerNamePrefix))
        //    { 
        //        var tc = tableService.GetTableClient(ti.Name);
        //        foreach (var te in tc.Query<TableEntity>())
        //            await tc.DeleteEntityAsync(te.PartitionKey, te.RowKey);
        //    }
        //}
                
    }
}