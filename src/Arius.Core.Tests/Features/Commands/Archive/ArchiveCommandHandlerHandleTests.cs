using Arius.Core.Features.Commands.Archive;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Arius.Core.Tests.Helpers.FakeLogger;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;
using Zio;
using System.Linq;

namespace Arius.Core.Tests.Features.Commands.Archive;

public class ArchiveCommandHandlerHandleTests : IClassFixture<FixtureWithFileSystem>
{
    private readonly FixtureWithFileSystem             fixture;
    private readonly FakeLogger<ArchiveCommandHandler> logger;
    private readonly ArchiveCommandHandler             handler;

    public ArchiveCommandHandlerHandleTests(FixtureWithFileSystem fixture)
    {
        this.fixture = fixture;
        logger       = new FakeLogger<ArchiveCommandHandler>();
        handler      = new ArchiveCommandHandler(logger, NullLoggerFactory.Instance, fixture.AriusConfiguration);
    }

    [Fact]
    public async Task Handle_WithSmallAndLargeFiles_ShouldUploadChunksAndState()
    {
        // Arrange
        var smallFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "small.txt")
            .WithRandomContent(512, seed: 1)
            .Build();

        var largeFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "large.bin")
            .WithRandomContent(4096, seed: 2)
            .Build();

        var progressUpdates = new List<ProgressUpdate>();
        var progressReporter = new Progress<ProgressUpdate>(progressUpdates.Add);

        var command = new ArchiveCommandBuilder(fixture)
            .WithProgressReporter(progressReporter)
            .WithHashingParallelism(1)
            .WithUploadParallelism(1)
            .WithSmallFileBoundary(1024)
            .Build();

        var archiveStorage = new FakeArchiveStorage();
        var loggerFactory  = new FakeLoggerFactory();

        var handlerContext = await new HandlerContextBuilder(command, loggerFactory)
            .WithArchiveStorage(archiveStorage)
            .BuildAsync();

        var expectedInitialFileCount = handlerContext.FileSystem
            .EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories)
            .Count();

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(2);
        summary.UniqueChunksUploaded.ShouldBe(2);
        summary.PointerFilesCreated.ShouldBe(2);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.ExistingPointerFiles.ShouldBe(0);
        summary.BytesUploadedUncompressed.ShouldBe(smallFile.OriginalContent.Length + largeFile.OriginalContent.Length);
        summary.NewStateName.ShouldNotBeNull();

        archiveStorage.StoredChunks.Count.ShouldBe(2);
        archiveStorage.UploadedStates.ShouldContain(summary.NewStateName!);

        var tarChunk = archiveStorage.StoredChunks.Values.Single(c => c.ContentType == "application/aes256cbc+tar+gzip");
        tarChunk.Metadata.ShouldContainKey("OriginalContentLength");
        tarChunk.Metadata.ShouldContainKey("SmallChunkCount");
        tarChunk.Metadata["SmallChunkCount"].ShouldBe("1");

        var largeChunk = archiveStorage.StoredChunks.Values.Single(c => c.ContentType == "application/aes256cbc+gzip");
        largeChunk.Metadata.ShouldContainKey("OriginalContentLength");
        largeChunk.Metadata["OriginalContentLength"].ShouldBe(largeFile.OriginalContent.Length.ToString());

        var smallPointerPath = Path.Join(fixture.TestRunSourceFolder.FullName, "small.txt.pointer.arius");
        File.Exists(smallPointerPath).ShouldBeTrue();

        var largePointerPath = Path.Join(fixture.TestRunSourceFolder.FullName, "large.bin.pointer.arius");
        File.Exists(largePointerPath).ShouldBeTrue();

        handlerContext.StateRepository.GetPointerFileEntry("/small.txt.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry("/large.bin.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WithDuplicateLargeFiles_ShouldUploadBinaryOnceAndCreateMultiplePointers()
    {
        // Arrange
        var originalLargeFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "shared.bin")
            .WithRandomContent(4096, seed: 42)
            .Build();

        _ = new FakeFileBuilder(fixture)
            .WithDuplicate(originalLargeFile, UPath.Root / "duplicates" / "shared-copy.bin")
            .Build();

        var command = new ArchiveCommandBuilder(fixture)
            .WithHashingParallelism(1)
            .WithUploadParallelism(1)
            .WithSmallFileBoundary(1024)
            .Build();

        var archiveStorage = new FakeArchiveStorage();
        var loggerFactory  = new FakeLoggerFactory();

        var handlerContext = await new HandlerContextBuilder(command, loggerFactory)
            .WithArchiveStorage(archiveStorage)
            .BuildAsync();

        var expectedInitialFileCount = handlerContext.FileSystem
            .EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories)
            .Count();

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(2);
        summary.BytesUploadedUncompressed.ShouldBe(originalLargeFile.OriginalContent.Length);

        archiveStorage.StoredChunks.Count.ShouldBe(1);
        var chunk = archiveStorage.StoredChunks.Values.Single();
        chunk.Metadata.ShouldContainKey("OriginalContentLength");
        chunk.Metadata["OriginalContentLength"].ShouldBe(originalLargeFile.OriginalContent.Length.ToString());

        File.Exists(Path.Combine(fixture.TestRunSourceFolder.FullName, "shared.bin.pointer.arius")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.TestRunSourceFolder.FullName, "duplicates", "shared-copy.bin.pointer.arius")).ShouldBeTrue();

        handlerContext.StateRepository.GetPointerFileEntry("/shared.bin.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry("/duplicates/shared-copy.bin.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WhenStateRepositoryContainsStalePointer_ShouldRemoveIt()
    {
        // Arrange
        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "active.txt")
            .WithRandomContent(256, seed: 7)
            .Build();

        var command = new ArchiveCommandBuilder(fixture)
            .WithHashingParallelism(1)
            .WithUploadParallelism(1)
            .WithSmallFileBoundary(1024)
            .Build();

        var archiveStorage = new FakeArchiveStorage();
        var loggerFactory  = new FakeLoggerFactory();

        var handlerContext = await new HandlerContextBuilder(command, loggerFactory)
            .WithArchiveStorage(archiveStorage)
            .BuildAsync();

        var expectedInitialFileCount = handlerContext.FileSystem
            .EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories)
            .Count();

        var staleHash = FakeHashBuilder.GenerateValidHash(99);
        handlerContext.StateRepository.AddBinaryProperties(new BinaryProperties
        {
            Hash         = staleHash,
            OriginalSize = 1,
            ArchivedSize = 1,
            StorageTier  = StorageTier.Cool
        });
        handlerContext.StateRepository.UpsertPointerFileEntries(new PointerFileEntry
        {
            Hash             = staleHash,
            RelativeName     = "/stale.bin.pointer.arius",
            CreationTimeUtc  = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow
        });

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.PointerFileEntriesDeleted.ShouldBe(1);
        handlerContext.StateRepository.GetPointerFileEntry("/stale.bin.pointer.arius")
            .ShouldBeNull();

        archiveStorage.StoredChunks.Count.ShouldBe(1);
        archiveStorage.UploadedStates.ShouldNotBeEmpty();

        File.Exists(Path.Combine(fixture.TestRunSourceFolder.FullName, "active.txt.pointer.arius")).ShouldBeTrue();
    }
}
