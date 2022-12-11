using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Commands;
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
    public static readonly DateTime UnitTestGracePeriod = new(2022, 1, 1);

    public const string Passphrase = "myPassphrase";
    private const string TestContainerNamePrefix = "unittest";

    public static string AccountName { get; set; }
    public static string AccountKey { get; set; }

    public static Facade Facade { get; set; }

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
        PrestageSourceFolder();

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

        
        // Initialize Facade
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());

        var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
        {
            TempDirectoryName = ".ariustemp",
            RestoreTempDirectoryName = ".ariusrestore"
        });

        Facade = new Facade(loggerFactory, tempDirectoryAppSettings);
    }



    public class SourceFilesType
    {
        //public static string File1 = "FILE1";
        //public static string File2 = "FILE2";
        //public static string File3Large = "FILE3";
        //public static string File4WithSpace = "FILE4";
        //public static string File5Deduplicated = "FILE5";

        private SourceFilesType(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }
        public string Value { get; }


        private const string File1Value = "FILE1";
        private const string File2Value = "FILE2";
        private const string File3LargeValue = "FILE3";
        private const string File4WithSpaceValue = "FILE4";
        private const string File5DeduplicatedValue = "FILE5";

        public static SourceFilesType File1 { get; } = new SourceFilesType(File1Value);
        public static SourceFilesType File2 { get; } = new SourceFilesType(File2Value);
        public static SourceFilesType File3Large { get; } = new SourceFilesType(File3LargeValue);
        public static SourceFilesType File4WithSpace { get; } = new SourceFilesType(File4WithSpaceValue);
        public static SourceFilesType File5Deduplicated { get; } = new SourceFilesType(File5DeduplicatedValue);
    }

    private static void PrestageSourceFolder()
    {
        files.Add(SourceFilesType.File1.Value, new(() => CreateRandomFile(Path.Combine(SourceFolder.FullName, "dir 1", "file 1.txt"), 512000 + 1))); //make it an odd size to test buffer edge cases
        files.Add(SourceFilesType.File2.Value, new(() => CreateRandomFile(Path.Combine(SourceFolder.FullName, "dir 1", "file 2.doc"), 2 * 1024 * 1024)));
        files.Add(SourceFilesType.File3Large.Value, new(() => CreateRandomFile(Path.Combine(SourceFolder.FullName, "dir 1", "file 3 large.txt"), 10 * 1024 * 1024)));
        files.Add(SourceFilesType.File4WithSpace.Value, new(() => CreateRandomFile(Path.Combine(SourceFolder.FullName, "dir 2", "file4 with space.txt"), 1 * 1024 * 1024)));
        files.Add(SourceFilesType.File5Deduplicated.Value, new(() =>
            {
                var f1 = files[SourceFilesType.File1.Value].Value.FullName;
                var f2 = files[SourceFilesType.File2.Value].Value.FullName;

                var f = Path.Combine(SourceFolder.FullName, "dir 2", "deduplicated file.txt"); //special file created out of two other files
                var concatenatedBytes = File.ReadAllBytes(f1).Concat(File.ReadAllBytes(f2));
                File.WriteAllBytes(f, concatenatedBytes.ToArray());

                return new FileInfo(f);
            }));
    }
    private static readonly Dictionary<string, Lazy<FileInfo>> files = new();

    public static void StageArchiveTestDirectory(out FileInfo fi, SourceFilesType type)
    {
        fi = files[type.Value].Value.CopyTo(SourceFolder, ArchiveTestDirectory);
    }
    public static void StageArchiveTestDirectory(out FileInfo fi, string key = null, int sizeInBytes = 2 * 1024 * 1024 + 1)
    {
        FileInfo source;
        if (key is not null && files.ContainsKey(key))
            source = files[key].Value;
        else
        {
            source = CreateRandomFile(Path.Combine(SourceFolder.FullName, "dir 3", DateTime.Now.Ticks.ToString()), sizeInBytes);
            if (key is not null)
                files.Add(key, new(source));
        }

        fi = source.CopyTo(SourceFolder, ArchiveTestDirectory);
    }
    public static void StageArchiveTestDirectory(out FileInfo[] fis)
    {
        fis = files.Values.Select(fi => fi.Value.CopyTo(SourceFolder, ArchiveTestDirectory)).ToArray();
    }



    public static FileInfo CreateRandomFile(string fileFullName, int sizeInBytes) // TODO make private
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
    }

    public static async Task PurgeRemote()
    {
        // delete all blobs in the container but leave the container
        await foreach (var bi in Container.GetBlobsAsync())
            await Container.DeleteBlobAsync(bi.Name);
    }
}