using Arius.Core.Features.Archive;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using System.Runtime.InteropServices;
using Arius.Core.Shared.Hashing;
using System.IO.Compression;
using System.Text;
using Shouldly;
using Azure.Storage.Blobs;
using Arius.Core.Shared.StateRepositories;
using Azure.Storage.Blobs.Models;
using Azure.Storage;

namespace Arius.Core.Tests.Features.Archive;

public class ArchiveCommandHandlerTests : IClassFixture<FixtureWithFileSystem>
{
    private readonly FixtureWithFileSystem             fixture;
    private readonly FakeLogger<ArchiveCommandHandler> logger;
    private readonly ArchiveCommandHandler             handler;

    public ArchiveCommandHandlerTests(FixtureWithFileSystem fixture)
    {
        this.fixture = fixture;
        logger       = new();
        handler      = new ArchiveCommandHandler(logger, NullLoggerFactory.Instance, fixture.AriusConfiguration);
    }


    [Fact]
    public async Task RunArchiveCommand() // NOTE this one is skipped in CI
    {
        var logger = new FakeLogger<ArchiveCommandHandler>();

        // TODO Make this better
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var c = new ArchiveCommandBuilder(fixture)
            .WithLocalRoot(isWindows ? 
                new DirectoryInfo("C:\\Users\\WouterVanRanst\\Downloads\\Photos-001 (1)") : 
                new DirectoryInfo("/mnt/c/Users/WouterVanRanst/Downloads/Photos-001 (1)"))
            .Build();
        await handler.Handle(c, CancellationToken.None);

    }

    [Fact(Skip = "TODO")]
    public void UpdatedCreationTimeOrLastWriteTimeShouldBeUpdatedInStateDatabase()
    {

    }

    [Fact]
    public async Task UploadIfNotExistsAsync_WhenFileDoesNotExist_ShouldUploadSuccessfully()
    {
        // Arrange
        var testContent = "test content for new upload";
        var sourceStream = new MemoryStream(Encoding.UTF8.GetBytes(testContent));
        var hash = CreateTestHash(1);
        var expectedContentType = "application/x-arius-chunk+gzip";
        var compressionLevel = CompressionLevel.Optimal;

        var handlerContext = await CreateHandlerContextAsync();

        // Act
        var result = await handler.UploadIfNotExistsAsync(
            handlerContext, hash, sourceStream, compressionLevel, expectedContentType, CancellationToken.None);

        // Assert
        result.OriginalSize.ShouldBeGreaterThan(0);
        result.ArchivedSize.ShouldBeGreaterThan(0);

        // Verify the blob was actually created
        var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, CancellationToken.None);
        properties.ShouldNotBeNull();
        properties.ContentType.ShouldBe(expectedContentType);
    }

    [Fact]
    public async Task UploadIfNotExistsAsync_WhenFileExistsWithCorrectContentType_ShouldReturnExistingMetadata()
    {
        // Arrange
        var testContent = "test content for existing blob";
        var sourceStream = new MemoryStream(Encoding.UTF8.GetBytes(testContent));
        var hash = CreateTestHash(2);
        var expectedContentType = "application/x-arius-chunk+gzip";
        var compressionLevel = CompressionLevel.Optimal;

        var handlerContext = await CreateHandlerContextAsync();

        // First upload to create the blob
        await handler.UploadIfNotExistsAsync(
            handlerContext, hash, sourceStream, compressionLevel, expectedContentType, CancellationToken.None);

        // Reset stream for second call
        sourceStream.Seek(0, SeekOrigin.Begin);

        // Act - Second call should detect existing blob
        var result = await handler.UploadIfNotExistsAsync(
            handlerContext, hash, sourceStream, compressionLevel, expectedContentType, CancellationToken.None);

        // Assert
        result.OriginalSize.ShouldBeGreaterThan(0);
        result.ArchivedSize.ShouldBeGreaterThan(0);

        // Verify properties are still correct
        var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, CancellationToken.None);
        properties.ShouldNotBeNull();
        properties.ContentType.ShouldBe(expectedContentType);
    }

    [Fact]
    public async Task UploadIfNotExistsAsync_WhenFileExistsWithWrongContentType_ShouldDeleteAndReUpload()
    {
        // Arrange
        if (fixture.RepositoryOptions == null)
            return; // Skip if no Azure configuration

        var testContent = "test content for corrupted blob";
        var sourceStream = new MemoryStream(Encoding.UTF8.GetBytes(testContent));
        var hash = CreateTestHash(3);
        var wrongContentType = "text/plain";
        var correctContentType = "application/x-arius-chunk+gzip";
        var compressionLevel = CompressionLevel.Optimal;

        var handlerContext = await CreateHandlerContextAsync();

        // Create a blob with wrong content type using BlobClient directly (simulating corruption)
        var blobServiceClient = new BlobServiceClient(
            new Uri($"https://{fixture.RepositoryOptions.AccountName}.blob.core.windows.net"),
            new Azure.Storage.StorageSharedKeyCredential(fixture.RepositoryOptions.AccountName, fixture.RepositoryOptions.AccountKey));

        var containerClient = blobServiceClient.GetBlobContainerClient(fixture.RepositoryOptions.ContainerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobClient = containerClient.GetBlobClient(hash.ToString());

        // Upload blob with wrong content type
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = wrongContentType }
        };
        await blobClient.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes("corrupted content")), uploadOptions, CancellationToken.None);

        // Verify wrong content type was set
        var initialProperties = await blobClient.GetPropertiesAsync();
        initialProperties.Value.ContentType.ShouldBe(wrongContentType);

        // Reset source stream
        sourceStream.Seek(0, SeekOrigin.Begin);

        // Act - Should detect wrong content type, delete, and re-upload
        var result = await handler.UploadIfNotExistsAsync(
            handlerContext, hash, sourceStream, compressionLevel, correctContentType, CancellationToken.None);

        // Assert
        result.OriginalSize.ShouldBeGreaterThan(0);
        result.ArchivedSize.ShouldBeGreaterThan(0);

        // Verify the blob now has correct content type
        var finalProperties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, CancellationToken.None);
        finalProperties.ShouldNotBeNull();
        finalProperties.ContentType.ShouldBe(correctContentType);
    }

    private async Task<HandlerContext> CreateHandlerContextAsync()
    {
        var command = new ArchiveCommandBuilder(fixture)
            .WithLocalRoot(fixture.TestRunSourceFolder)
            .Build();

        var handlerContextBuilder = new HandlerContextBuilder(command, NullLoggerFactory.Instance);
        return await handlerContextBuilder.BuildAsync();
    }

    private static Hash CreateTestHash(int seed)
    {
        var bytes = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            bytes[i] = (byte)(seed + i);
        }
        return Hash.FromBytes(bytes);
    }
}