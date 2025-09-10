using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.FakeLogger;
using Arius.Core.Tests.Helpers.Fixtures;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Shouldly;

namespace Arius.Core.Tests.Shared.Storage;

public class AzureBlobStorageTests : IClassFixture<Fixture>
{
    private readonly BlobContainerClient containerClient;
    private readonly AzureBlobStorage azureBlobStorage;

    public AzureBlobStorageTests(Fixture fixture)
    {
        var blobServiceClient = new BlobServiceClient(new Uri($"https://{fixture.RepositoryOptions.AccountName}.blob.core.windows.net"), new Azure.Storage.StorageSharedKeyCredential(fixture.RepositoryOptions.AccountName, fixture.RepositoryOptions.AccountKey));

        containerClient = blobServiceClient.GetBlobContainerClient(fixture.RepositoryOptions.ContainerName);
        containerClient.CreateIfNotExists();

        // Create AzureBlobStorage instance
        var logger = new FakeLoggerFactory().CreateLogger<AzureBlobStorage>();
        azureBlobStorage = new AzureBlobStorage(fixture.RepositoryOptions.AccountName, fixture.RepositoryOptions.AccountKey, fixture.RepositoryOptions.ContainerName, false, logger);
    }

    [Fact]
    public async Task OpenReadAsync_WhenBlobExists_ShouldReturnOkWithStream()
    {
        // Arrange
        var blobName    = "blob1";
        var testContent = "test content for blob1";

        var blobClient      = containerClient.GetBlobClient(blobName);

        // Upload test blob
        await blobClient.UploadAsync(BinaryData.FromString(testContent), overwrite: true);

        // Act
        var result = await azureBlobStorage.OpenReadAsync(blobName);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        using var stream  = result.Value;
        using var reader  = new StreamReader(stream);
        var       content = await reader.ReadToEndAsync();
        content.ShouldBe(testContent);
    }

    [Fact]
    public async Task OpenReadAsync_WhenBlobNotFound_ShouldReturnBlobNotFoundError()
    {
        // Arrange
        var blobName = "blob2";

        // Act
        var result = await azureBlobStorage.OpenReadAsync(blobName);

        // Assert
        result.IsFailed.ShouldBeTrue();
        var error = result.Errors.Single().ShouldBeOfType<BlobNotFoundError>();
        error.BlobName.ShouldBe(blobName);
    }

    [Fact]
    public async Task OpenReadAsync_WhenBlobIsArchived_ShouldReturnBlobArchivedError()
    {
        // Arrange
        var blobName    = "blob3";
        var testContent = "test content for archived blob";
        var blobClient      = containerClient.GetBlobClient(blobName);

        // Upload and archive blob
        await blobClient.UploadAsync(BinaryData.FromString(testContent), overwrite: true);
        await blobClient.SetAccessTierAsync(AccessTier.Archive);

        //// Wait for tier change to take effect
        //await Task.Delay(3000);

        // Act
        var result = await azureBlobStorage.OpenReadAsync(blobName);

        // Assert
        result.IsFailed.ShouldBeTrue();
        result.Errors.Single().ShouldBeOfType<BlobArchivedError>();
    }

    [Fact]
    public async Task OpenReadAsync_WhenBlobIsRehydrating_ShouldReturnBlobRehydratingError()
    {
        // Arrange
        var blobName    = "blob4";
        var testContent = "test content for rehydrating blob";
        var blobClient      = containerClient.GetBlobClient(blobName);

        // Upload and archive blob
        await blobClient.UploadAsync(BinaryData.FromString(testContent), overwrite: true);
        await blobClient.SetAccessTierAsync(AccessTier.Archive);

        // Wait for archive to take effect
        //await Task.Delay(500);

        // Start rehydration to Hot tier
        await blobClient.SetAccessTierAsync(AccessTier.Hot, rehydratePriority: Azure.Storage.Blobs.Models.RehydratePriority.Standard);


        // Act - Immediately try to read while rehydrating
        var result = await azureBlobStorage.OpenReadAsync(blobName);

        // Assert
        result.IsFailed.ShouldBeTrue();
        result.Errors.Single().ShouldBeOfType<BlobRehydratingError>();
    }
}