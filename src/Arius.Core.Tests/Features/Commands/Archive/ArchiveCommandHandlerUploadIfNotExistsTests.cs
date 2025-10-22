using Arius.Core.Features.Commands.Archive;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;
using System.IO.Compression;
using System.Text;

namespace Arius.Core.Tests.Features.Commands.Archive;

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


    //[Fact]
    //[Trait("Category", "SkipCI")]
    //public async Task RunArchiveCommandTEMP() // NOTE TEMP this one is skipped in CI via the SkipCI category
    //{
    //    var logger = new FakeLogger<ArchiveCommandHandler>();

    //    // TODO Make this better
    //    var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    //    var c = new ArchiveCommandBuilder(fixture)
    //        .WithLocalRoot(isWindows ? 
    //            new DirectoryInfo("C:\\Users\\WouterVanRanst\\Downloads\\Photos-001 (1)") : 
    //            new DirectoryInfo("/mnt/c/Users/WouterVanRanst/Downloads/Photos-001 (1)"))
    //        .Build();
    //    await handler.Handle(c, CancellationToken.None);
    //}

    [Fact(Skip = "TODO")]
    public void UpdatedCreationTimeOrLastWriteTimeShouldBeUpdatedInStateDatabase()
    {
    }

    // --- UPLOADIFNOTEXIST

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
        var result = await handler.UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, expectedContentType, null, CancellationToken.None);


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
        await handler.UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, expectedContentType, null, CancellationToken.None);

        await handlerContext.ArchiveStorage.SetChunkStorageTierPerPolicy(hash, 0, StorageTier.Hot); // Set to Hot tier to check if the correct storage tier was applied afterwards

            // Reset stream for second call
        sourceStream.Seek(0, SeekOrigin.Begin);


        // Act - Second call should detect existing blob
        var result = await handler.UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, expectedContentType, null, CancellationToken.None);


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
        var result = await handler.UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, correctContentType, null, CancellationToken.None);


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

    [Fact]
    public async Task UploadIfNotExistsAsync_WhenTarArchive_ShouldIncludeSmallChunkCount()
    {
        // Arrange
        var testContent         = "test content for TAR archive";
        var sourceStream        = new MemoryStream(Encoding.UTF8.GetBytes(testContent));
        var hash                = FakeHashBuilder.GenerateValidHash(4);
        var expectedContentType = "application/aes256cbc+tar+gzip";
        var compressionLevel    = CompressionLevel.NoCompression; // TAR is already compressed
        var expectedChunkCount  = 5;

        var additionalMetadata = new Dictionary<string, string>
        {
            ["SmallChunkCount"] = expectedChunkCount.ToString()
        };

        var handlerContext = await CreateHandlerContextAsync();

        // Act
        var result = await handler.UploadIfNotExistsAsync(handlerContext, hash, sourceStream, compressionLevel, expectedContentType, additionalMetadata, CancellationToken.None);

        // Assert
        //result.OriginalSize.ShouldBeGreaterThan(0);
        //result.ArchivedSize.ShouldBeGreaterThan(0);

            // Verify the blob was created with correct properties and metadata
        var properties = await handlerContext.ArchiveStorage.GetChunkPropertiesAsync(hash, CancellationToken.None);
        properties.ShouldNotBeNull();
        //properties.ContentType.ShouldBe(expectedContentType);

            // Verify metadata includes both OriginalContentLength and SmallChunkCount
        properties.Metadata.ShouldNotBeNull();
        //properties.Metadata.ShouldContainKey("OriginalContentLength");
        //properties.Metadata["OriginalContentLength"].ShouldBe(result.OriginalSize.ToString());

        properties.Metadata.ShouldContainKey("SmallChunkCount");
        properties.Metadata["SmallChunkCount"].ShouldBe(expectedChunkCount.ToString());

            // Verify correct contentlength
        properties.ContentLength.ShouldBe(result.ArchivedSize);

            // Verify Storage Tier
        properties.StorageTier.ShouldBe(StorageTier.Cool);
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