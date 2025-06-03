using Arius.Core.Commands;
using Arius.Core.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Testing;
using Xunit.Abstractions;

namespace Arius.Core.Tests;

public class AzureBlobStorageTests
{
    private readonly ITestOutputHelper output;
    private readonly Fixture           fixture;

    public AzureBlobStorageTests(ITestOutputHelper output)
    {
        this.output = output;
        fixture = new Fixture();
    }

    [Fact]
    public void DeleteLocalDb()
    {
        var stateDatabaseFile = new FileInfo("state.db");
        stateDatabaseFile.Delete();

        File.Delete(@"C:\Users\WouterVanRanst\Documents\GitHub\Arius 4\src-Arius5\Arius.Core.Tests\bin\Debug\net8.0\state.db");
    }

    //[Fact]
    //public async Task Hah()
    //{
    //    var afs = new AggregateFileSystem();

    //    var pfs = new PhysicalFileSystem();
    //    var sfs1 = new SubFileSystem(pfs, pfs.ConvertPathFromInternal("C:\\Users\\RFC430\\Downloads\\New folder"));
    //    var sfs2 = new SubFileSystem(pfs, pfs.ConvertPathFromInternal("C:\\Users\\RFC430\\OneDrive\\Pictures\\Screenshots"));

    //    afs.AddFileSystem(sfs1);
    //    afs.AddFileSystem(sfs2);

    //    var x = afs.EnumerateFileEntries(UPath.Root).ToList();
    //}

    [Fact]
    public async Task CleanupAzure()
    {
        var blobServiceClient = new BlobServiceClient($"DefaultEndpointsProtocol=https;AccountName={fixture.RepositoryOptions.AccountName};AccountKey={fixture.RepositoryOptions.AccountKey};EndpointSuffix=core.windows.net");

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
        var c = new ArchiveCommand
        {
            AccountName   = fixture.RepositoryOptions.AccountName,
            AccountKey    = fixture.RepositoryOptions.AccountKey,
            ContainerName = fixture.RepositoryOptions.ContainerName ?? "atest",
            Passphrase    = fixture.RepositoryOptions.Passphrase,
            RemoveLocal   = false,
            Tier          = StorageTier.Cool,
            LocalRoot     = new DirectoryInfo("C:\\Users\\WouterVanRanst\\Downloads\\Photos-001 (1)")
        };

        var logger = new FakeLogger<ArchiveCommandHandler>();
        var ch     = new ArchiveCommandHandler(logger);
        await ch.Handle(c, CancellationToken.None);

    }



}


