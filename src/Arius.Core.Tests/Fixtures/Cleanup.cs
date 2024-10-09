using System.Globalization;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.Tests.Fixtures;

public class Cleanup : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return new FixtureBuilder()
            .WithRealStorageAccountFactory()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }


    [Fact]
    public async Task CleanupAzureAsync()
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
    public async Task CleanupLocalAsync()
    {
        if (!Fixture.TestRunRootFolder.Parent.Exists)
            return;
        
        foreach (var subdirectory in Fixture.TestRunRootFolder.Parent.GetDirectories())
        {
            var folderName   = subdirectory.Name;
            var dateTimePart = folderName.Split('-')[0];

            if (!DateTime.TryParseExact(dateTimePart, "yyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var folderDateTime)) 
                continue;

            var timeDifference = DateTime.UtcNow - folderDateTime;

            if (timeDifference.TotalHours > 24)
            {
                subdirectory.Delete(true);
            }
        }
    }
}