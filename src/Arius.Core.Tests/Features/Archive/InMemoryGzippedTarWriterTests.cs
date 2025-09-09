using Arius.Core.Features.Archive;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Tests.Helpers.Fixtures;
using Shouldly;
using System.Formats.Tar;
using System.IO.Compression;

namespace Arius.Core.Tests.Features.Archive;

public class InMemoryGzippedTarWriterTests : IClassFixture<Fixture>, IDisposable
{
    private readonly Fixture fixture;
    private readonly Sha256Hasher hasher;

    public InMemoryGzippedTarWriterTests(Fixture fixture)
    {
        this.fixture = fixture;
        this.hasher = new Sha256Hasher(Fixture.PASSPHRASE);
    }

    //[Fact]
    //public async Task AddEntryAsync_ShouldAddFileToArchive()
    //{
    //    // Arrange
    //    var testData = fixture.FileSystem.WithSourceFolderHavingFilePair("/test.txt", FilePairType.BinaryFileOnly, 11);
    //    var testFile = testData.FilePair.BinaryFile;
        
    //    using var tarWriter = new InMemoryGzippedTarWriter(hasher);
    //    var initialPosition = tarWriter.Position;

    //    // Act
    //    var archivedSize = await tarWriter.AddEntryAsync(testFile, "test-entry", CancellationToken.None);

    //    // Assert
    //    archivedSize.ShouldBeGreaterThan(0L);
    //    tarWriter.Position.ShouldBeGreaterThan(initialPosition);
    //}

    [Fact]
    public async Task AddEntryAsync_MultipleCalls_ShouldIncreasePosition()
    {
        // Arrange
        var testData1 = fixture.FileSystem.WithSourceFolderHavingFilePair("/test1.txt", FilePairType.BinaryFileOnly, 13);
        var testData2 = fixture.FileSystem.WithSourceFolderHavingFilePair("/test2.txt", FilePairType.BinaryFileOnly, 13);
        var testFile1 = testData1.FilePair.BinaryFile;
        var testFile2 = testData2.FilePair.BinaryFile;

        using var tarWriter = new InMemoryGzippedTarWriter(hasher);
        var initialPosition = tarWriter.Position;

        // Act
        var archivedSize1 = await tarWriter.AddEntryAsync(testFile1, "entry1", CancellationToken.None);
        var positionAfterFirst = tarWriter.Position;
        
        var archivedSize2 = await tarWriter.AddEntryAsync(testFile2, "entry2", CancellationToken.None);
        var finalPosition = tarWriter.Position;

        // Assert
        archivedSize1.ShouldBeGreaterThan(0L);
        archivedSize2.ShouldBeGreaterThan(0L);
        positionAfterFirst.ShouldBeGreaterThan(initialPosition);
        finalPosition.ShouldBeGreaterThan(positionAfterFirst);
    }

    [Fact]
    public async Task GetCompletedArchive_ShouldReturnValidTarGzipStream()
    {
        // Arrange
        var testContent = "Hello World from TAR test";
        var testData = fixture.FileSystem.WithSourceFolderHavingFilePair("/test.txt", FilePairType.BinaryFileOnly, testContent.Length);
        var testFile = testData.FilePair.BinaryFile;
        testFile.WriteAllText(testContent); // Override with our specific content

        using var tarWriter = new InMemoryGzippedTarWriter(hasher);
        await tarWriter.AddEntryAsync(testFile, "test-entry", CancellationToken.None);

        // Act
        await using var archiveStream = tarWriter.GetCompletedArchive();

        // Assert
        archiveStream.ShouldNotBeNull();
        archiveStream.CanRead.ShouldBeTrue();
        archiveStream.Position.ShouldBe(0L);
        archiveStream.Length.ShouldBeGreaterThan(0L);

        // Verify we can read the TAR content back
        await using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);
        
        var entry = await tarReader.GetNextEntryAsync(copyData: true, CancellationToken.None);
        entry.ShouldNotBeNull();
        entry.Name.ShouldBe("test-entry");
        
        if (entry.DataStream != null)
        {
            using var reader = new StreamReader(entry.DataStream);
            var readContent = await reader.ReadToEndAsync();
            readContent.ShouldBe(testContent);
        }
    }

    [Fact]
    public async Task GetArchiveHashAsync_ShouldReturnConsistentHash()
    {
        // Arrange
        var testData = fixture.FileSystem.WithSourceFolderHavingFilePair("/test.txt", FilePairType.BinaryFileOnly, 11);
        var testFile = testData.FilePair.BinaryFile;

        using var tarWriter = new InMemoryGzippedTarWriter(hasher);
        await tarWriter.AddEntryAsync(testFile, "test-entry", CancellationToken.None);

        // Act
        var hash1 = await tarWriter.GetArchiveHashAsync();
        var hash2 = await tarWriter.GetArchiveHashAsync();

        // Assert
        hash1.ShouldNotBeNull();
        hash2.ShouldNotBeNull();
        hash1.ShouldBe(hash2); // Should be consistent
    }

    //[Fact]
    //public async Task Dispose_ShouldHandleResourceCleanup()
    //{
    //    // Arrange
    //    var testData = fixture.FileSystem.WithSourceFolderHavingFilePair("/test.txt", FilePairType.BinaryFileOnly, 11);
    //    var testFile = testData.FilePair.BinaryFile;

    //    var tarWriter = new InMemoryGzippedTarWriter(hasher);
    //    await tarWriter.AddEntryAsync(testFile, "test-entry", CancellationToken.None);

    //    // Act & Assert - Should not throw
    //    tarWriter.Dispose();
        
    //    // Multiple dispose calls should be safe
    //    tarWriter.Dispose();
    //}

    //[Fact]
    //public async Task AddEntryAsync_AfterDispose_ShouldThrowObjectDisposedException()
    //{
    //    // Arrange
    //    var testData = fixture.FileSystem.WithSourceFolderHavingFilePair("/test.txt", FilePairType.BinaryFileOnly, 11);
    //    var testFile = testData.FilePair.BinaryFile;

    //    var tarWriter = new InMemoryGzippedTarWriter(hasher);
    //    tarWriter.Dispose();

    //    // Act & Assert
    //    await Should.ThrowAsync<ObjectDisposedException>(() =>
    //        tarWriter.AddEntryAsync(testFile, "test-entry", CancellationToken.None));
    //}

    public void Dispose()
    {
        hasher?.Dispose();
    }
}