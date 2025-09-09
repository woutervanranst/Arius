using Arius.Core.Features.Archive;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Shouldly;
using System.Formats.Tar;
using System.IO.Compression;

namespace Arius.Core.Tests.Features.Archive;

public class InMemoryGzippedTarWriterTests : IClassFixture<Fixture>
{
    private readonly Fixture fixture;

    public InMemoryGzippedTarWriterTests(Fixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task AddEntryAsync_MultipleCalls_ShouldIncreasePosition()
    {
        // Arrange
        var testData1 = new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/test1.txt").WithRandomContent(10, 1).Build();
        var testData2 = new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/test2.txt").WithRandomContent(1024, 2).Build();
        var testData3 = new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/test3.txt").WithRandomContent(1024 * 1024, 3).Build();

        using var tarWriter       = new InMemoryGzippedTarWriter(CompressionLevel.SmallestSize);
        var       positionInitial = tarWriter.Position;

        // Act
        var archivedSize1 = await tarWriter.AddEntryAsync(testData1.FilePair.BinaryFile, "entry1", CancellationToken.None);
        var positionAfter1 = tarWriter.Position;
        
        var archivedSize2 = await tarWriter.AddEntryAsync(testData2.FilePair.BinaryFile, "entry2", CancellationToken.None);
        var positionAfter2 = tarWriter.Position;

        var archivedSize3 = await tarWriter.AddEntryAsync(testData3.FilePair.BinaryFile, "entry3", CancellationToken.None);
        var positionAfter3 = tarWriter.Position;

        await using var archiveStream = tarWriter.GetCompletedArchive();

        // Assert
        // -- Verify positions and sizes make sense
        archivedSize1.ShouldBeGreaterThan(0L);
        archivedSize2.ShouldBeGreaterThan(0L);
        archivedSize3.ShouldBeGreaterThan(0L);
        positionAfter1.ShouldBeGreaterThan(positionInitial);
        positionAfter2.ShouldBeGreaterThan(positionAfter1);
        positionAfter3.ShouldBeGreaterThan(positionAfter2);


        // -- Verify we have a valid archive stream
        archiveStream.ShouldNotBeNull();
        archiveStream.CanRead.ShouldBeTrue();
        archiveStream.Position.ShouldBe(0L);
        archiveStream.Length.ShouldBeGreaterThan(0L);


        // -- Verify we can read the TAR content back
        await using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
        await using var tarReader  = new TarReader(gzipStream);

        var entry1 = await tarReader.GetNextEntryAsync(copyData: true, CancellationToken.None);
        entry1.ShouldNotBeNull();
        entry1.Name.ShouldBe("entry1");
        entry1.DataStream.ShouldNotBeNull();
        using var ms1 = new MemoryStream();
        await entry1.DataStream.CopyToAsync(ms1);
        ms1.ToArray().ShouldBe(testData1.OriginalContent);


        var entry2 = await tarReader.GetNextEntryAsync(copyData: true, CancellationToken.None);
        entry2.ShouldNotBeNull();
        entry2.Name.ShouldBe("entry2");
        entry2.DataStream.ShouldNotBeNull();
        using var ms2 = new MemoryStream();
        await entry2.DataStream.CopyToAsync(ms2);
        ms2.ToArray().ShouldBe(testData2.OriginalContent);


        var entry3 = await tarReader.GetNextEntryAsync(copyData: true, CancellationToken.None);
        entry3.ShouldNotBeNull();
        entry3.Name.ShouldBe("entry3");
        entry3.DataStream.ShouldNotBeNull();
        using var ms3 = new MemoryStream();
        await entry3.DataStream.CopyToAsync(ms3);
        ms3.ToArray().ShouldBe(testData3.OriginalContent);
    }
}