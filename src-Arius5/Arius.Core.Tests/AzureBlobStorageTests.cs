using Arius.Core.Commands;
using Arius.Core.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace FileSystem.Local.Tests;

public class AzureBlobStorageTests
{
    private readonly ITestOutputHelper _output;

    public AzureBlobStorageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DeleteLocalDb()
    {
        var stateDatabaseFile = new FileInfo("state.db");
        stateDatabaseFile.Delete();

        File.Delete(@"C:\Repos\Arius\src-Arius5\Arius.Cli\bin\Debug\net8.0\state.db");
    }

    //[Fact]
    //public async Task Hah()
    //{
    //    var afs = new AggregateFileSystem();

    //    var pfs = new PhysicalFileSystem();
    //    var sfs1 = new SubFileSystem(pfs, pfs.ConvertPathFromInternal("C:\\Users\\RFC430\\Downloads\\New folder"));
    //    var sfs2 = new SubFileSystem(pfs, pfs.ConvertPathFromInternal("C:\\Users\\RFC430\\OneDrive - Fluvius cvba\\Pictures\\Screenshots"));

    //    afs.AddFileSystem(sfs1);
    //    afs.AddFileSystem(sfs2);

    //    var x = afs.EnumerateFileEntries(UPath.Root).ToList();
    //}

    private TestRemoteRepositoryOptions GetTestRemoteRepositoryOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<AzureBlobStorageTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetSection("RepositoryOptions").Get<TestRemoteRepositoryOptions>();
    }

    [Fact]
    public async Task CleanupAzure()
    {
        var config = GetTestRemoteRepositoryOptions();

        var blobServiceClient = new BlobServiceClient($"DefaultEndpointsProtocol=https;AccountName={config.AccountName};AccountKey={config.AccountKey};EndpointSuffix=core.windows.net");

        // List all containers
        await foreach (var container in blobServiceClient.GetBlobContainersAsync())
        {
            // Get container properties to check LastModified date
            var containerClient = blobServiceClient.GetBlobContainerClient(container.Name);
            var properties      = (BlobContainerProperties)await containerClient.GetPropertiesAsync();

            // Check if the container was last modified more than 24 hours ago
            if (properties.LastModified < DateTimeOffset.UtcNow.AddHours(-24))
            {
                // Delete container
                await containerClient.DeleteAsync();
            }
        }
    }

    [Fact]
    public async Task RunArchiveCommand()
    {
        var config = GetTestRemoteRepositoryOptions();

        var c = new ArchiveCommand
        {
            AccountName   = config.AccountName,
            AccountKey    = config.AccountKey,
            ContainerName = config.ContainerName ?? "atest",
            Passphrase    = config.Passphrase,
            RemoveLocal   = false,
            Tier          = StorageTier.Cool,
            LocalRoot     = new DirectoryInfo("C:\\Users\\RFC430\\Downloads\\New folder")
        };

        var ch = new ArchiveCommandHandler();
        await ch.Handle(c, CancellationToken.None);

    }

    public record TestRemoteRepositoryOptions
    {
        public string AccountName { get; init; }

        public string AccountKey { get; init; }

        //[JsonIgnore]
        public string ContainerName { get; set; }

        public string Passphrase { get; init; }
    }

}


