using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.New.UnitTests.Fixtures;

public class Cleanup : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return FixtureBuilder.Create().Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    
    [Fact]
    public async void CleanupAzureAsync()
    {
        var storageOptions    = Fixture.StorageAccountOptions;
        var blobServiceClient = new BlobServiceClient($"DefaultEndpointsProtocol=https;AccountName={storageOptions.AccountName};AccountKey={storageOptions.AccountKey};EndpointSuffix=core.windows.net");

        // List all containers
        await foreach (var container in blobServiceClient.GetBlobContainersAsync())
        {
            // Get container properties to check LastModified date
            var                     containerClient = blobServiceClient.GetBlobContainerClient(container.Name);
            BlobContainerProperties properties      = await containerClient.GetPropertiesAsync();

            // Check if the container was last modified more than 24 hours ago
            if (properties.LastModified < DateTimeOffset.UtcNow.AddHours(-24))
            {
                // Delete container
                await containerClient.DeleteAsync();
            }
        }
    }

    [Fact]
    public async void CleanupLocalAsync()
    {
        //if (Directory.Exists(Fixture.TestRunRootFolder.FullName))
        //{
        //    Fixture.TestRunRootFolder.Delete(true); // Recursively delete the test run folder
        //}
    }
}