using Arius.Core.Facade;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests;

[SetUpFixture]
internal static class TestSetup
{
    public static readonly DateTime UnitTestGracePeriod = new(2022, 1, 1);

    public const  string Passphrase              = "myPassphrase";
    private const string TEST_CONTAINER_NAME_PREFIX = "unittest";

    public static string AccountName { get; set; }
    public static string AccountKey  { get; set; }

    public static RepositoryFacade RepositoryFacade { get; set; }

    private static DirectoryInfo unitTestRoot;
    public static  DirectoryInfo SourceFolder         { get; private set; }
    public static  DirectoryInfo ArchiveTestDirectory { get; private set; }
    public static  DirectoryInfo RestoreTestDirectory { get; private set; }

    private static BlobServiceClient   blobService;
    public static  BlobContainerClient Container { get; private set; }


    [OneTimeSetUp]
    public static async Task OneTimeSetup()
    {
        // Executes once before the test run. (Optional)
        
        var containerName = $"{TEST_CONTAINER_NAME_PREFIX}-{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}";
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
        Container = await blobService.CreateBlobContainerAsync(containerName);


        // Initialize Facade
        RepositoryFacade = await new Facade.Facade(NullLoggerFactory.Instance)
            .ForStorageAccount(TestSetup.AccountName, TestSetup.AccountKey)
            .ForRepositoryAsync(TestSetup.Container.Name, TestSetup.Passphrase);
    }

    [OneTimeTearDown]
    public static async Task OneTimeTearDown()
    {
        // TODO delete SQLite file?

        // Delete local temp
        foreach (var d in new DirectoryInfo(Path.GetTempPath()).GetDirectories($"{TEST_CONTAINER_NAME_PREFIX}*")) 
            d.Delete(true);

        // Delete blobs
        foreach (var bci in blobService.GetBlobContainers(prefix: $"{TEST_CONTAINER_NAME_PREFIX}-{DateTime.Now.AddHours(-1):yyMMddHHmmss}"))
            await blobService.GetBlobContainerClient(bci.Name).DeleteAsync();
    }


    public class SourceFilesType
    {
        private SourceFilesType(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }
        public string Value { get; }


        private const string FILE1_VALUE = "FILE1";
        private const string FILE2_VALUE = "FILE2";
        private const string FILE3_LARGE_VALUE = "FILE3";
        private const string FILE4_WITH_SPACE_VALUE = "FILE4";
        private const string FILE_5DEDUPLICATED_VALUE = "FILE5";

        public static SourceFilesType File1 { get; } = new SourceFilesType(FILE1_VALUE);
        public static SourceFilesType File2 { get; } = new SourceFilesType(FILE2_VALUE);
        public static SourceFilesType File3Large { get; } = new SourceFilesType(FILE3_LARGE_VALUE);
        public static SourceFilesType File4WithSpace { get; } = new SourceFilesType(FILE4_WITH_SPACE_VALUE);
        public static SourceFilesType File5Deduplicated { get; } = new SourceFilesType(FILE_5DEDUPLICATED_VALUE);
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
        f.Directory.CreateIfNotExists();

        byte[] data = new byte[sizeInBytes];
        var rng = new Random();
        rng.NextBytes(data);
        File.WriteAllBytes(fileFullName, data);

        return f;
    }
    
    public static async Task PurgeRemote()
    {
        // delete all blobs in the container but leave the container
        await foreach (var bi in Container.GetBlobsAsync())
            await Container.DeleteBlobAsync(bi.Name);
    }
}