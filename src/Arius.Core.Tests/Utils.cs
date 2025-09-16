using Arius.Core.Tests.Helpers.Fixtures;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Xunit.Abstractions;

namespace Arius.Core.Tests;

public class Utils : IClassFixture<Fixture>
{
    private readonly ITestOutputHelper output;
    private readonly Fixture           fixture;

    public Utils(Fixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output  = output;
    }

    [Fact(Skip = "To check")]
    public void CleanupLocalDb()
    {
        var stateDatabaseFile = new FileInfo("state.db");
        stateDatabaseFile.Delete();
    }

    [Fact]
    public void CleanupLocalTemp()
    {
        var cutoff = DateTime.UtcNow.AddDays(-2);

        foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), "Arius.Core.Tests*"))
        {
            var info = new DirectoryInfo(dir);

            if (info.CreationTimeUtc < cutoff)
            {
                info.Delete(recursive: true);
            }
        }
    }

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

            if (containerClient.Name == "books") // h4x0r for testing
                continue; 

            // Check if the container was last modified more than 24 hours ago
            if (properties.LastModified < DateTimeOffset.UtcNow.AddHours(-24))
            {
                // Delete container
                await containerClient.DeleteAsync();
            }
        }
    }
}


