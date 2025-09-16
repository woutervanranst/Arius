using Arius.Core.Features.Archive;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fixtures;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Arius.Core.Tests.Helpers.Fakes;

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
    public async Task RunArchiveCommandTEMP() // NOTE TEMP this one is skipped in CI
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
    public async Task UploadIfNotExistsAsync_WhenChunkDoesNotExist_ShouldUpload()
    {
        // Arrange
        var testContent         = "test content for new upload";
        var sourceStream        = new MemoryStream(Encoding.UTF8.GetBytes(testContent));
        var hash                = FakeHashBuilder.GenerateValidHash(1);
        var expectedContentType = "application/x-arius-chunk+gzip";
        var compressionLevel    = CompressionLevel.Optimal;

        var handlerContext = await CreateHandlerContextAsync();

        // Act
        var result = await handler.UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, expectedContentType, CancellationToken.None);

        // Assert
        result.OriginalSize.ShouldBeGreaterThan(0);
        result.ArchivedSize.ShouldBeGreaterThan(0);

        // Verify the blob was actually created with correct properties and metadata
        var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, CancellationToken.None);
        properties.ShouldNotBeNull();
        properties.ContentType.ShouldBe(expectedContentType);

        // Verify metadata is read from storage and matches returned values
        properties.Metadata.ShouldNotBeNull();
        properties.Metadata.ShouldContainKey("OriginalContentLength");
        properties.Metadata["OriginalContentLength"].ShouldBe(result.OriginalSize.ToString());

        // Verify correct contentlength
        properties.ContentLength.ShouldBe(result.ArchivedSize);

        // Verify Storage Tier
        properties.StorageTier.ShouldBe(StorageTier.Cool);

        // Verify the stream was read to the end (ie the binary was uploaded)
        sourceStream.Position.ShouldBe(sourceStream.Length);
    }

    [Fact]
    public async Task UploadIfNotExistsAsync_WhenValidChunkExists_ShouldNotUploadAgain()
    {
        // Arrange
        var testContent         = "test content for existing blob";
        var sourceStream        = new MemoryStream(Encoding.UTF8.GetBytes(testContent));
        var hash                = FakeHashBuilder.GenerateValidHash(2);
        var expectedContentType = "application/x-arius-chunk+gzip";
        var compressionLevel    = CompressionLevel.Optimal;

        var handlerContext = await CreateHandlerContextAsync();

        // First upload to create the blob
        await handler.UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, expectedContentType, CancellationToken.None);

        await handlerContext.ArchiveStorage.SetChunkStorageTierPerPolicy(hash, 0, StorageTier.Hot); // Set to Hot tier to check if the correct storage tier was applied afterwards

        // Reset stream for second call
        sourceStream.Seek(0, SeekOrigin.Begin);

        // Act - Second call should detect existing blob
        var result = await handler.UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, expectedContentType, CancellationToken.None);

        // Assert
        result.OriginalSize.ShouldBeGreaterThan(0);
        result.ArchivedSize.ShouldBeGreaterThan(0);

        // Verify properties are still correct and metadata is read from storage
        var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, CancellationToken.None);
        properties.ShouldNotBeNull();
        properties.ContentType.ShouldBe(expectedContentType);

        // Verify metadata is read from storage and matches returned values
        properties.Metadata.ShouldNotBeNull();
        properties.Metadata.ShouldContainKey("OriginalContentLength");
        properties.Metadata["OriginalContentLength"].ShouldBe(result.OriginalSize.ToString());

        // Verify correct contentlength
        properties.ContentLength.ShouldBe(result.ArchivedSize);

        // Verify Storage Tier
        properties.StorageTier.ShouldBe(StorageTier.Cool);

        // Verify the stream was NOT read (ie the binary was NOT uploaded again)
        sourceStream.Position.ShouldBe(0);
    }

    [Fact]
    public async Task UploadIfNotExistsAsync_WhenInvalidChunk_ShouldDeleteAndReUpload()
    {
        // Arrange
        var testContent        = "test content for corrupted blob";
        var sourceStream       = new MemoryStream(Encoding.UTF8.GetBytes(testContent));
        var hash               = FakeHashBuilder.GenerateValidHash(3);
        var correctContentType = "application/x-arius-chunk+gzip";
        var compressionLevel   = CompressionLevel.Optimal;

        var handlerContext = await CreateHandlerContextAsync();

        // Create a blob with wrong content type using BlobClient directly (simulating corruption)
        var blobServiceClient = new BlobServiceClient(new Uri($"https://{fixture.RepositoryOptions.AccountName}.blob.core.windows.net"), new Azure.Storage.StorageSharedKeyCredential(fixture.RepositoryOptions.AccountName, fixture.RepositoryOptions.AccountKey));

        var containerClient = blobServiceClient.GetBlobContainerClient(fixture.RepositoryOptions.ContainerName);
        var blobClient      = containerClient.GetBlobClient($"chunks/{hash}");

        // Upload blob without metadata
        var uploadOptions = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = correctContentType } };
        await blobClient.UploadAsync(new MemoryStream("corrupted content"u8.ToArray()), uploadOptions, CancellationToken.None);

        // Reset source stream
        sourceStream.Seek(0, SeekOrigin.Begin);

        // Act - Should detect wrong content type, delete, and re-upload
        var result = await handler.UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, correctContentType, CancellationToken.None);

        // Assert
        result.OriginalSize.ShouldBeGreaterThan(0);
        result.ArchivedSize.ShouldBeGreaterThan(0);

        // Verify the blob now has correct content type and metadata
        var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, CancellationToken.None);
        properties.ShouldNotBeNull();
        properties.ContentType.ShouldBe(correctContentType);

        // Verify metadata is read from storage and matches returned values
        properties.Metadata.ShouldNotBeNull();
        properties.Metadata.ShouldContainKey("OriginalContentLength");
        properties.Metadata["OriginalContentLength"].ShouldBe(result.OriginalSize.ToString());

        // Verify correct contentlength
        properties.ContentLength.ShouldBe(result.ArchivedSize);

        // Verify Storage Tier
        properties.StorageTier.ShouldBe(StorageTier.Cool);

        // Verify the stream was read to the end (ie the binary was uploaded again)
        sourceStream.Position.ShouldBe(sourceStream.Length);
    }

    private async Task<HandlerContext> CreateHandlerContextAsync()
    {
        var command = new ArchiveCommandBuilder(fixture)
            .WithLocalRoot(fixture.TestRunSourceFolder)
            .Build();

        var handlerContextBuilder = new HandlerContextBuilder(command, NullLoggerFactory.Instance);
        return await handlerContextBuilder.BuildAsync();
    }
}