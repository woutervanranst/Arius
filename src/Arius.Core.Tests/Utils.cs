using Arius.Core.Tests.Helpers.Fixtures;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Xunit.Abstractions;

namespace Arius.Core.Tests;

public class Utils
{
    private readonly ITestOutputHelper output;
    private readonly InMemoryFileSystemFixture fixture;

    public Utils(ITestOutputHelper output)
    {
        this.output = output;
        fixture = new InMemoryFileSystemFixture();
    }

    [Fact]
    public void CleanupLocalDb()
    {
        var stateDatabaseFile = new FileInfo("state.db");
        stateDatabaseFile.Delete();
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

            // Check if the container was last modified more than 24 hours ago
            if (properties.LastModified < DateTimeOffset.UtcNow.AddHours(-24))
            {
                // Delete container
                await containerClient.DeleteAsync();
            }
        }
    }
}


