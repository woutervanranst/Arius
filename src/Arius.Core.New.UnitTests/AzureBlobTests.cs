using Arius.Core.Infrastructure.Storage.Azure;
using Arius.Core.New.UnitTests.Fixtures;
using Azure;
using FluentAssertions;

namespace Arius.Core.New.UnitTests;

public class AzureBlobTests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return FixtureBuilder.Create()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    private AzureBlob GetAzureBlob()
    {
        var r = (AzureCloudRepository)Fixture.CloudRepository;
        return r.ChunksFolder.GetBlob(Guid.NewGuid().ToString());
    }
    
    private const string contentType = "testcontenttype";

    [Fact]
    public async Task OpenWriteAsync_ShouldUploadBlobWithCorrectMetadata()
    {
        // Arrange
        var azureBlob = GetAzureBlob();
        var metadata = AzureBlob.CreateMetadata(123);

        // Act
        var s = await azureBlob.OpenWriteAsync(contentType, metadata);

        // Assert
        var retrievedMetadata = await azureBlob.GetMetadataAsync();
        Assert.Equal("123", retrievedMetadata[AzureBlob.ORIGINAL_CONTENT_LENGTH_METADATA_KEY]);
        (await azureBlob.GetOriginalContentLengthAsync()).Should().Be(123);
    }

    [Fact]
    public async Task OpenWriteAsync_ShouldUploadBlobWithoutMetadata()
    {
        // Arrange
        var azureBlob = GetAzureBlob();

        // Act
        await using (var stream = await azureBlob.OpenWriteAsync(contentType))
        {
            var writer = new StreamWriter(stream);
            await writer.WriteAsync("This is a test blob without metadata");
            await writer.FlushAsync();
        }

        // Assert
        var retrievedMetadata = await azureBlob.GetMetadataAsync();
        Assert.Empty(retrievedMetadata);
    }

    [Fact]
    public async Task OpenWriteAsync_ShouldUploadBlobWithCorrectContentType()
    {
        // Arrange
        var azureBlob = GetAzureBlob();

        // Act
        await using (var stream = await azureBlob.OpenWriteAsync(contentType))
        {
            var writer = new StreamWriter(stream);
            await writer.WriteAsync("This is a test blob with a content type");
            await writer.FlushAsync();
        }

        // Assert
        var retrievedContentType = await azureBlob.GetContentTypeAsync();
        Assert.Equal(contentType, retrievedContentType);
    }

    [Fact]
    public async Task OpenWriteAsync_ShouldOverwriteContentType_WhenChanged()
    {
        // Arrange
        var azureBlob      = GetAzureBlob();
        var newContentType = "application/json";

        // Write with the initial content type
        await using (var stream = await azureBlob.OpenWriteAsync(contentType))
        {
        }

        // Act: Overwrite with a new content type
        await using (var stream = await azureBlob.OpenWriteAsync(throwOnExists: false, contentType: newContentType))
        {
        }

        // Assert
        var retrievedContentType = await azureBlob.GetContentTypeAsync();
        Assert.Equal(newContentType, retrievedContentType);
    }

    [Fact]
    public async Task OpenWriteAsync_ThrowsException_WhenExists()
    {
        // Arrange
        var b = GetAzureBlob();
        
        var blob = await b.OpenWriteAsync();

        // Act
        async Task Act() => await b.OpenWriteAsync(throwOnExists: true);

        // Assert
        await Assert.ThrowsAsync<RequestFailedException>(Act);
    }
}