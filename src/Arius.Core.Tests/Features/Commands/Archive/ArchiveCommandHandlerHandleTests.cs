using Arius.Core.Features.Commands.Archive;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.FakeLogger;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;
using Zio;

namespace Arius.Core.Tests.Features.Commands.Archive;

public class ArchiveCommandHandlerHandleTests
{
    private readonly FixtureWithFileSystem fixture;

    public ArchiveCommandHandlerHandleTests()
    {
        this.fixture = new();
    }

    private ArchiveCommandHandler CreateHandler() => new(new FakeLogger<ArchiveCommandHandler>(), NullLoggerFactory.Instance, fixture.AriusConfiguration);

    private const int DefaultSmallFileBoundary = 1024;

    private static string ToRelativePointerPath(UPath binaryPath) => binaryPath.GetPointerFilePath().ToString();

    private static string ToAbsolutePointerPath(FixtureWithFileSystem fixture, UPath binaryPath) => Path.Combine(fixture.TestRunSourceFolder.FullName, binaryPath.GetPointerFilePath().ToString().TrimStart('/'));

    private static (int FileCount, int ExistingPointerCount) GetInitialFileStatistics(HandlerContext handlerContext)
    {
        var entries = handlerContext.FileSystem
            .EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories)
            .Select(FilePair.FromBinaryFileFileEntry)
            .ToList();

        var existingPointerFileCount = entries.Count(fp => fp.PointerFile.Exists);

        return (entries.Count, existingPointerFileCount);
    }

    private async Task<(ArchiveCommand Command, HandlerContext Context, MockArchiveStorageBuilder StorageBuilder, FakeLoggerFactory LoggerFactory)> CreateHandlerContextAsync(Action<ArchiveCommandBuilder>? configureCommand = null,
        MockArchiveStorageBuilder? storageBuilder = null,
        FakeLoggerFactory? loggerFactory = null)
    {
        storageBuilder ??= new MockArchiveStorageBuilder(fixture);
        loggerFactory  ??= new FakeLoggerFactory();

        var commandBuilder = new ArchiveCommandBuilder(fixture)
            .WithSmallFileBoundary(DefaultSmallFileBoundary)
            .WithHashingParallelism(1)
            .WithUploadParallelism(1);

        configureCommand?.Invoke(commandBuilder);

        var command        = commandBuilder.Build();
        var archiveStorage = storageBuilder.Build();

        Retry:
        try
        {
            var handlerContext = await new HandlerContextBuilder(command, loggerFactory)
                .WithArchiveStorage(archiveStorage)
                .BuildAsync();

            return (command, handlerContext, storageBuilder, loggerFactory);
        }
        catch (IOException)
        {
            await Task.Delay(100); // Delay until the statefile name ("yyyy-MM-ddTHH-mm-ss") is in different seconds
            goto Retry;
        }

        
    }

    [Fact(Skip = "TODO")]
    public void UpdatedCreationTimeOrLastWriteTimeShouldBeUpdatedInStateDatabase()
    {
    }


    // --- SINGLE FILE

    [Fact]
    public async Task Single_LargeFile_FirstUpload_ShouldUploadBinaryAndCreatePointer()
    {
        // Arrange
        var binaryPath = UPath.Root / "documents" / "presentation.pptx";
        var largeFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .WithRandomContent(4096, seed: 10)
            .Build();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        var (expectedInitialFileCount, expectedExistingPointerFile) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(1);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.ExistingPointerFiles.ShouldBe(expectedExistingPointerFile);
        summary.BytesUploadedUncompressed.ShouldBe(largeFile.OriginalContent.LongLength);
        summary.NewStateName.ShouldNotBeNull();

        storageBuilder.StoredChunks.Count.ShouldBe(1);
        var chunk = storageBuilder.StoredChunks.Single().Value;
        chunk.ContentType.ShouldBe("application/aes256cbc+gzip");
        chunk.Metadata.ShouldContainKey("OriginalContentLength");
        chunk.Metadata["OriginalContentLength"].ShouldBe(largeFile.OriginalContent.Length.ToString());

        File.Exists(ToAbsolutePointerPath(fixture, binaryPath)).ShouldBeTrue();

        var pointerEntry = handlerContext.StateRepository
            .GetPointerFileEntry(ToRelativePointerPath(binaryPath), includeBinaryProperties: true);
        pointerEntry.ShouldNotBeNull();
        pointerEntry!.Hash.ShouldBe(largeFile.OriginalHash);
        pointerEntry.BinaryProperties.ShouldNotBeNull();
        pointerEntry.BinaryProperties.OriginalSize.ShouldBe(largeFile.OriginalContent.LongLength);
        pointerEntry.BinaryProperties.ArchivedSize.ShouldBe(chunk.ContentLength);
        pointerEntry.BinaryProperties.ParentHash.ShouldBeNull();

        var binaryProperties = handlerContext.StateRepository.GetBinaryProperty(pointerEntry.Hash);
        binaryProperties.ShouldNotBeNull();
        binaryProperties!.ArchivedSize.ShouldBe(chunk.ContentLength);
    }

    [Fact]
    public async Task Single_SmallFile_FirstUpload_ShouldCreateTarParentAndChildBinaryProperties()
    {
        // Arrange
        var binaryPath = UPath.Root / "notes" / "small.txt";
        var smallFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .WithRandomContent(512, seed: 2)
            .Build();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        var (expectedInitialFileCount, expectedExistingPointerFile) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(1);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.ExistingPointerFiles.ShouldBe(expectedExistingPointerFile);
        summary.BytesUploadedUncompressed.ShouldBe(smallFile.OriginalContent.LongLength);
        summary.NewStateName.ShouldNotBeNull();

        storageBuilder.StoredChunks.Count.ShouldBe(1);
        var tarChunk = storageBuilder.StoredChunks.Single().Value;
        tarChunk.ContentType.ShouldBe("application/aes256cbc+tar+gzip");
        tarChunk.Metadata.ShouldContainKey("OriginalContentLength");
        tarChunk.Metadata.ShouldContainKey("SmallChunkCount");
        tarChunk.Metadata["OriginalContentLength"].ShouldBe(tarChunk.ContentLength.ToString());
        tarChunk.Metadata["SmallChunkCount"].ShouldBe("1");

        File.Exists(ToAbsolutePointerPath(fixture, binaryPath)).ShouldBeTrue();

        var pointerEntry = handlerContext.StateRepository
            .GetPointerFileEntry(ToRelativePointerPath(binaryPath), includeBinaryProperties: true);
        pointerEntry.ShouldNotBeNull();
        pointerEntry!.BinaryProperties.ShouldNotBeNull();
        pointerEntry.BinaryProperties.Hash.ShouldBe(smallFile.OriginalHash);
        pointerEntry.BinaryProperties.ParentHash.ShouldNotBeNull();
        pointerEntry.BinaryProperties.OriginalSize.ShouldBe(smallFile.OriginalContent.LongLength);
        pointerEntry.BinaryProperties.ArchivedSize.ShouldBeGreaterThan(0L);

        var parentHash = pointerEntry.BinaryProperties.ParentHash!;
        var parentProperties = handlerContext.StateRepository.GetBinaryProperty(parentHash);
        parentProperties.ShouldNotBeNull();
        parentProperties!.OriginalSize.ShouldBe(smallFile.OriginalContent.LongLength);
        parentProperties.ArchivedSize.ShouldBe(tarChunk.ContentLength);
        tarChunk.Metadata["OriginalContentLength"].ShouldBe(parentProperties.ArchivedSize.ToString());

        handlerContext.StateRepository.GetBinaryProperty(pointerEntry.Hash).ShouldNotBeNull();
    }

    [Fact]
    public async Task Single_EmptyFile_ShouldUploadZeroLengthBinary()
    {
        // Arrange
        var binaryPath = UPath.Root / "empty" / "file.bin";
        var emptyFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .Build();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        var (expectedInitialFileCount, expectedExistingPointerFile) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(1);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.ExistingPointerFiles.ShouldBe(expectedExistingPointerFile);
        summary.BytesUploadedUncompressed.ShouldBe(0);
        summary.NewStateName.ShouldNotBeNull();

        storageBuilder.StoredChunks.Count.ShouldBe(1);
        var tarChunk = storageBuilder.StoredChunks.Single().Value;
        tarChunk.ContentType.ShouldBe("application/aes256cbc+tar+gzip");
        tarChunk.Metadata.ShouldContainKey("SmallChunkCount");
        tarChunk.Metadata["SmallChunkCount"].ShouldBe("1");

        File.Exists(ToAbsolutePointerPath(fixture, binaryPath)).ShouldBeTrue();

        var pointerEntry = handlerContext.StateRepository
            .GetPointerFileEntry(ToRelativePointerPath(binaryPath), includeBinaryProperties: true);
        pointerEntry.ShouldNotBeNull();
        pointerEntry!.BinaryProperties.ShouldNotBeNull();
        pointerEntry.BinaryProperties.OriginalSize.ShouldBe(0);
        pointerEntry.BinaryProperties.ArchivedSize.ShouldBeGreaterThanOrEqualTo(0L);

        var parentHash = pointerEntry.BinaryProperties.ParentHash!;
        handlerContext.StateRepository.GetBinaryProperty(parentHash).ShouldNotBeNull();
    }

    [Fact]
    public async Task Single_BinaryWithExistingPointer_ShouldOverwritePointerAndTrackExistingCount()
    {
        // Arrange
        var binaryPath = UPath.Root / "existing" / "document.pdf";
        var binaryWithPointer = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .WithRandomContent(2048, seed: 5)
            .Build();

        var staleHash = FakeHashBuilder.GenerateValidHash(42);
        staleHash.ShouldNotBe(binaryWithPointer.OriginalHash);
        var stalePointer = binaryWithPointer.FilePair.CreatePointerFile(staleHash);
        stalePointer.ReadHash().ShouldBe(staleHash);

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        var (expectedInitialFileCount, expectedExistingPointerFile) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(0);
        summary.ExistingPointerFiles.ShouldBe(expectedExistingPointerFile);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.BytesUploadedUncompressed.ShouldBe(binaryWithPointer.OriginalContent.LongLength);
        summary.NewStateName.ShouldNotBeNull();

        storageBuilder.StoredChunks.Count.ShouldBe(1);

        var pointerPath = ToAbsolutePointerPath(fixture, binaryPath);
        File.Exists(pointerPath).ShouldBeTrue();

        var updatedHash = binaryWithPointer.FilePair.PointerFile.ReadHash();
        updatedHash.ShouldBe(binaryWithPointer.OriginalHash);

        var pointerEntry = handlerContext.StateRepository
            .GetPointerFileEntry(ToRelativePointerPath(binaryPath), includeBinaryProperties: true);
        pointerEntry.ShouldNotBeNull();
        pointerEntry!.Hash.ShouldBe(binaryWithPointer.OriginalHash);

        var binaryProperties = handlerContext.StateRepository.GetBinaryProperty(pointerEntry.Hash);
        binaryProperties.ShouldNotBeNull();
        binaryProperties!.ArchivedSize.ShouldBeGreaterThan(0L);
    }


    [Fact]
    public async Task Single_LatentPointer_ShouldLogWarning()
    {
        // Arrange
        var latentFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.PointerFileOnly, UPath.Root / "latent.txt")
            .WithRandomContent(512, seed: 1)
            .Build();

        latentFile.FilePair.BinaryFile.Exists.ShouldBeFalse();
        latentFile.FilePair.PointerFile.Exists.ShouldBeTrue();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.FilesSkipped.ShouldBe(1);
        summary.Warnings.ShouldContain("File '/latent.txt' is a pointer file without an associated binary, skipping");

        handlerContext.StateRepository.HasChanges.ShouldBeFalse();
        handlerContext.StateRepository.StateDatabaseFile.Exists.ShouldBeFalse();
    }


    // --- MULTIPLE FILES

    [Fact]
    public async Task Multiple_AllUnique_MixedSizes_ShouldUploadLargeAndSmallBatches()
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

        var (command, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync(builder => builder
            .WithProgressReporter(progressReporter)
            .WithHashingParallelism(1)
            .WithUploadParallelism(1)
            .WithSmallFileBoundary(DefaultSmallFileBoundary));

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

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

        storageBuilder.StoredChunks.Count.ShouldBe(2);
        storageBuilder.UploadedStates.ShouldContain(summary.NewStateName!);

        var tarChunk = storageBuilder.StoredChunks.Values.Single(c => c.ContentType == "application/aes256cbc+tar+gzip");
        tarChunk.Metadata.ShouldContainKey("OriginalContentLength");
        tarChunk.Metadata.ShouldContainKey("SmallChunkCount");
        tarChunk.Metadata["SmallChunkCount"].ShouldBe("1");

        var largeChunk = storageBuilder.StoredChunks.Values.Single(c => c.ContentType == "application/aes256cbc+gzip");
        largeChunk.Metadata.ShouldContainKey("OriginalContentLength");
        largeChunk.Metadata["OriginalContentLength"].ShouldBe(largeFile.OriginalContent.Length.ToString());

        var smallPointerPath = Path.Combine(fixture.TestRunSourceFolder.FullName, "small.txt.pointer.arius");
        File.Exists(smallPointerPath).ShouldBeTrue();

        var largePointerPath = Path.Combine(fixture.TestRunSourceFolder.FullName, "large.bin.pointer.arius");
        File.Exists(largePointerPath).ShouldBeTrue();

        handlerContext.StateRepository.GetPointerFileEntry("/small.txt.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry("/large.bin.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task Multiple_WithDuplicates_InSameRun_ShouldUploadBinaryOnceAndCreateMultiplePointers()
    {
        // Arrange
        var originalLargeFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "shared.bin")
            .WithRandomContent(4096, seed: 42)
            .Build();

        _ = new FakeFileBuilder(fixture)
            .WithDuplicate(originalLargeFile, UPath.Root / "duplicates" / "shared-copy.bin")
            .Build();

        var originalSmallFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "texts" / "note.txt")
            .WithRandomContent(320, seed: 7)
            .Build();

        _ = new FakeFileBuilder(fixture)
            .WithDuplicate(originalSmallFile, UPath.Root / "texts" / "archive" / "note-copy.txt")
            .Build();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(2); // large owner + small owner
        summary.UniqueChunksUploaded.ShouldBe(2);   // large chunk + TAR
        summary.PointerFilesCreated.ShouldBe(4);
        summary.BytesUploadedUncompressed.ShouldBe(
            originalLargeFile.OriginalContent.Length + originalSmallFile.OriginalContent.Length);

        storageBuilder.StoredChunks.Count.ShouldBe(2);
        storageBuilder.StoredChunks.Values.Count(c => c.ContentType == "application/aes256cbc+gzip").ShouldBe(1);
        storageBuilder.StoredChunks.Values.Count(c => c.ContentType == "application/aes256cbc+tar+gzip").ShouldBe(1);

        var largeChunk = storageBuilder.StoredChunks.Values.Single(c => c.ContentType == "application/aes256cbc+gzip");
        largeChunk.Metadata.ShouldContainKey("OriginalContentLength");
        largeChunk.Metadata["OriginalContentLength"].ShouldBe(originalLargeFile.OriginalContent.Length.ToString());

        var tarChunk = storageBuilder.StoredChunks.Values.Single(c => c.ContentType == "application/aes256cbc+tar+gzip");
        tarChunk.Metadata.ShouldContainKey("SmallChunkCount");
        tarChunk.Metadata["SmallChunkCount"].ShouldBe("1");

        File.Exists(Path.Combine(fixture.TestRunSourceFolder.FullName, "shared.bin.pointer.arius")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.TestRunSourceFolder.FullName, "duplicates", "shared-copy.bin.pointer.arius")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.TestRunSourceFolder.FullName, "texts", "note.txt.pointer.arius")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.TestRunSourceFolder.FullName, "texts", "archive", "note-copy.txt.pointer.arius")).ShouldBeTrue();

        handlerContext.StateRepository.GetPointerFileEntry("/shared.bin.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry("/duplicates/shared-copy.bin.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry("/texts/note.txt.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry("/texts/archive/note-copy.txt.pointer.arius", includeBinaryProperties: true)
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task Multiple_SmallFiles_SingleTarBatch_ShouldUploadSingleParentChunk()
    {
        // Arrange
        var paths = new[]
        {
            UPath.Root / "tar" / "alpha.txt",
            UPath.Root / "tar" / "beta.txt",
            UPath.Root / "tar" / "gamma.txt"
        };

        var smallFiles = paths
            .Select((path, index) => new FakeFileBuilder(fixture)
                .WithActualFile(FilePairType.BinaryFileOnly, path)
                .WithRandomContent(256 + index * 10, seed: 100 + index)
                .Build())
            .ToArray();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(paths.Length);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(paths.Length);
        summary.BytesUploadedUncompressed.ShouldBe(smallFiles.Sum(f => f.OriginalContent.Length));
        summary.PointerFileEntriesDeleted.ShouldBe(0);

        storageBuilder.StoredChunks.Count.ShouldBe(1);
        var tarChunk = storageBuilder.StoredChunks.Single().Value;
        tarChunk.ContentType.ShouldBe("application/aes256cbc+tar+gzip");
        tarChunk.Metadata.ShouldContainKey("SmallChunkCount");
        tarChunk.Metadata["SmallChunkCount"].ShouldBe(paths.Length.ToString());

        foreach (var path in paths)
        {
            var pointerPath = ToAbsolutePointerPath(fixture, path);
            File.Exists(pointerPath).ShouldBeTrue();
            handlerContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(path), includeBinaryProperties: true)
                .ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Multiple_SmallFiles_MultipleTarBatches_ShouldFlushWhenBoundaryExceeded()
    {
        // We will hit the UploadSmallFileAsync / OWNER path with this test

        // Arrange
        var paths = new[]
        {
            UPath.Root / "tar" / "alpha.bin",
            UPath.Root / "tar" / "beta.bin",
            UPath.Root / "tar" / "gamma.bin"
        };

        var sizes = new[] { 600, 600, 600 }; // note: esp. for small binaries the TAR overhead is substantial

        var smallFiles = paths
            .Select((path, index) => new FakeFileBuilder(fixture)
                .WithActualFile(FilePairType.BinaryFileOnly, path)
                .WithRandomContent(sizes[index], seed: index)
                .Build())
            .ToArray();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(paths.Length);
        summary.UniqueChunksUploaded.ShouldBe(2); // <-- the Tar Writer flushed when the boundary exceeded
        summary.PointerFilesCreated.ShouldBe(paths.Length);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.BytesUploadedUncompressed.ShouldBe(smallFiles.Sum(f => f.OriginalContent.Length));

        var tarChunks = storageBuilder.StoredChunks.Values
            .Where(c => c.ContentType == "application/aes256cbc+tar+gzip")
            .ToList();
        tarChunks.Count.ShouldBe(2);
        tarChunks.Select(c => int.Parse(c.Metadata["SmallChunkCount"]))
            .OrderBy(v => v)
            .ShouldBe(new[] { 1, 2 }); // we expect a TAR with one small chunk and one with two small chunks

        foreach (var path in paths)
        {
            File.Exists(ToAbsolutePointerPath(fixture, path)).ShouldBeTrue(); // the local binary still exists
            handlerContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(path), includeBinaryProperties: true)
                .ShouldNotBeNull(); // the pointerfileentry has been saved
        }
    }

    [Fact]
    public async Task Multiple_SmallFiles_WithDuplicates_CrossTarBatches_ShouldWriteDeferredPointers()
    {
        // We will hit the UploadSmallFileAsync / NON-OWNER path with this test

        // Arrange
        var f10 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "tar" / "alpha.txt")
            .WithRandomContent(600, seed: 1)
            .Build();
        var f11 = new FakeFileBuilder(fixture)
            .WithDuplicate(f10, UPath.Root / "tar" / "alpha-duplicate.txt")
            .Build();

        var f20 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "tar" / "beta.txt")
            .WithRandomContent(600, seed: 2)
            .Build();

        var f30 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "tar" / "omega.txt")
            .WithRandomContent(600, seed: 3)
            .Build();
        var f31 = new FakeFileBuilder(fixture)
            .WithDuplicate(f30, UPath.Root / "tar" / "omega-duplicate.txt")
            .Build();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(3);
        summary.UniqueChunksUploaded.ShouldBe(2);
        summary.PointerFilesCreated.ShouldBe(5);
        summary.PointerFileEntriesDeleted.ShouldBe(0);

        var tarChunks = storageBuilder.StoredChunks.Values
            .Where(c => c.ContentType == "application/aes256cbc+tar+gzip")
            .ToList();
        tarChunks.Count.ShouldBe(2);
        tarChunks.Select(c => int.Parse(c.Metadata["SmallChunkCount"]))
            .OrderBy(v => v)
            .ShouldBe([1, 2]);


        f10.FilePair.BinaryFile.Exists.ShouldBeTrue();
        f11.FilePair.BinaryFile.Exists.ShouldBeTrue();
        f20.FilePair.BinaryFile.Exists.ShouldBeTrue();
        f30.FilePair.BinaryFile.Exists.ShouldBeTrue();
        f31.FilePair.BinaryFile.Exists.ShouldBeTrue();

        handlerContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(f10.OriginalPath), includeBinaryProperties: true).ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(f11.OriginalPath), includeBinaryProperties: true).ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(f20.OriginalPath), includeBinaryProperties: true).ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(f30.OriginalPath), includeBinaryProperties: true).ShouldNotBeNull();
        handlerContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(f31.OriginalPath), includeBinaryProperties: true).ShouldNotBeNull();
    }


    // --- INCREMENTAL RUNS

    [Fact]
    public async Task Incremental_AllFilesAlreadyUploaded_ShouldSkipUploads()
    {
        // Arrange
        var binaryPath = UPath.Root / "incremental" / "presentation.pptx";
        var largeFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .WithRandomContent(4096, seed: 501)
            .Build();

        var storageBuilder = new MockArchiveStorageBuilder(fixture);

        var (_, initialContext, _, _) = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var (initialFileCount, _)     = GetInitialFileStatistics(initialContext);

        var result1 = await CreateHandler().Handle(initialContext, CancellationToken.None);

        result1.IsSuccess.ShouldBeTrue();

        storageBuilder.StoredChunks.Count.ShouldBe(1);
        storageBuilder.UploadedStates.Count.ShouldBe(1);

        // Corrupt pointer file to ensure it is rewritten on incremental run
        var staleHash = FakeHashBuilder.GenerateValidHash(999);
        staleHash.ShouldNotBe(largeFile.OriginalHash);
        largeFile.FilePair.CreatePointerFile(staleHash);

        var (_, incrementalContext, _, _)             = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(incrementalContext);

        // Act
        var result2 = await CreateHandler().Handle(incrementalContext, CancellationToken.None);

        // Assert
        result2.IsSuccess.ShouldBeTrue();
        var summary2 = result2.Value;

        summary2.TotalLocalFiles.ShouldBe(expectedFileCount);
        summary2.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary2.UniqueBinariesUploaded.ShouldBe(0); // <-- no additional binaries were uploaded
        summary2.UniqueChunksUploaded.ShouldBe(0); // <-- etc
        summary2.PointerFilesCreated.ShouldBe(0); // <-- etc
        summary2.PointerFileEntriesDeleted.ShouldBe(0);  // <-- etc
        summary2.BytesUploadedUncompressed.ShouldBe(0); // <-- etc
        
            // No new state was created & uploaded and the (temporary) database file was deleted
        summary2.NewStateName.ShouldBeNull();
        incrementalContext.StateRepository.HasChanges.ShouldBeFalse();
        incrementalContext.StateRepository.StateDatabaseFile.Exists.ShouldBeFalse();
        storageBuilder.UploadedStates.Count.ShouldBe(1);

            // Pointer file was corrected
        largeFile.FilePair.PointerFile.ReadHash().ShouldBe(largeFile.OriginalHash);

        storageBuilder.StoredChunks.Count.ShouldBe(1); // <-- no additional chunks were uploaded
    }

    [Fact]
    public async Task Incremental_MixOfNewAndExisting_ShouldUploadOnlyNewFiles()
    {
        // Arrange
        var existingFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "docs" / "existing.pdf")
            .WithRandomContent(4096, seed: 2001)
            .Build();

        var storageBuilder = new MockArchiveStorageBuilder(fixture);

        var (_, initialContext, _, _) = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var result1 = await CreateHandler().Handle(initialContext, CancellationToken.None);
        result1.IsSuccess.ShouldBeTrue();

        var newSmallFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "docs" / "new-note.txt")
            .WithRandomContent(512, seed: 2002)
            .Build();

        var (_, incrementalContext, _, _)             = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(incrementalContext);

        // Act
        var result2 = await CreateHandler().Handle(incrementalContext, CancellationToken.None);

        // Assert
        result2.IsSuccess.ShouldBeTrue();
        var summary = result2.Value;

        summary.TotalLocalFiles.ShouldBe(expectedFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(1);
        summary.BytesUploadedUncompressed.ShouldBe(newSmallFile.OriginalContent.Length);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        
            // A new state was created & uploaded
        summary.NewStateName.ShouldNotBeNull();
        incrementalContext.StateRepository.HasChanges.ShouldBeTrue();

        var pointerEntry = incrementalContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(newSmallFile.OriginalPath), includeBinaryProperties: true);
        pointerEntry.ShouldNotBeNull();
        pointerEntry!.BinaryProperties.ShouldNotBeNull();
        pointerEntry.BinaryProperties!.OriginalSize.ShouldBe(newSmallFile.OriginalContent.Length);

        storageBuilder.StoredChunks.Values.Count().ShouldBe(2);
    }

    [Fact]
    public async Task Incremental_FileAndPointerDeleted_PointerFileEntryDeleted()
    {
        // Arrange
        var deletedFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "docs" / "to-delete.txt")
            .WithRandomContent(2048, seed: 1)
            .Build();

        var storageBuilder = new MockArchiveStorageBuilder(fixture);

        var (_, context1, _, _) = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var result1 = await CreateHandler().Handle(context1, CancellationToken.None);
        result1.IsSuccess.ShouldBeTrue();

            // Delete the pointer and the binary
        deletedFile.FilePair.BinaryFile.Delete();
        deletedFile.FilePair.PointerFile.Delete();

        var (_, context2, _, _)             = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(context2);

        // Act
        var result2 = await CreateHandler().Handle(context2, CancellationToken.None);

        // Assert
        result2.IsSuccess.ShouldBeTrue();
        var summary2 = result2.Value;

        summary2.TotalLocalFiles.ShouldBe(0);
        summary2.ExistingPointerFiles.ShouldBe(0);
        summary2.UniqueBinariesUploaded.ShouldBe(0);
        summary2.PointerFileEntriesDeleted.ShouldBe(1);

            // A new state was uploaded
        summary2.NewStateName.ShouldNotBeNull();
        storageBuilder.UploadedStates.Count.ShouldBe(2);

            // The PointerFileEntry should not exist
        var pfe = context2.StateRepository.GetPointerFileEntry(deletedFile.FilePair.PointerFile.FullName, includeBinaryProperties: true);
        pfe.ShouldBeNull();

        context2.StateRepository.GetPointerFileEntries("/", false).ShouldBeEmpty();

            // The deleted chunks should still be present
        storageBuilder.StoredChunks.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Incremental_FileDeleted_PointerRemains_ShouldStillExist()
    {
        // Arrange
        var deletedBinary = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "docs" / "to-delete.txt")
            .WithRandomContent(2048, seed: 1)
            .Build();

        var storageBuilder = new MockArchiveStorageBuilder(fixture);

        var (_, context1, _, _) = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var result1 = await CreateHandler().Handle(context1, CancellationToken.None);
        result1.IsSuccess.ShouldBeTrue();

        // Delete the binary, the pointer remains
        deletedBinary.FilePair.BinaryFile.Delete();

        var (_, context2, _, _)                       = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(context2);

        // Act
        var result2 = await CreateHandler().Handle(context2, CancellationToken.None);

        // Assert
        result2.IsSuccess.ShouldBeTrue();
        var summary2 = result2.Value;

        summary2.TotalLocalFiles.ShouldBe(1);
        summary2.ExistingPointerFiles.ShouldBe(1);
        summary2.UniqueBinariesUploaded.ShouldBe(0);
        summary2.PointerFileEntriesDeleted.ShouldBe(0);

        //      A new state was uploaded
        summary2.NewStateName.ShouldBeNull();
        storageBuilder.UploadedStates.Count.ShouldBe(1);
    }


    [Fact]
    public async Task Incremental_FileModified_ShouldUploadNewBinaryAndPreserveOldBinary()
    {
        // Arrange
        var filePath = UPath.Root / "docs" / "mutable.bin";
        var originalFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, filePath)
            .WithRandomContent(3072, seed: 1)
            .Build();

        var storageBuilder = new MockArchiveStorageBuilder(fixture);

        var (_, context1, _, _) = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var result1 = await CreateHandler().Handle(context1, CancellationToken.None);
        result1.IsSuccess.ShouldBeTrue();

        var bp1 = context1.StateRepository.GetBinaryProperty(originalFile.OriginalHash);
        bp1.ShouldNotBeNull();

        //      Overwrite the file with new content
        var modifiedFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, filePath)
            .WithRandomContent(4000, seed: 2)
            .Build();

        var (_, context2, _, _)             = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(context2);

        // Act
        var result2 = await CreateHandler().Handle(context2, CancellationToken.None);

        // Assert
        result2.IsSuccess.ShouldBeTrue();
        var summary = result2.Value;

        summary.TotalLocalFiles.ShouldBe(1);
        summary.ExistingPointerFiles.ShouldBe(1);
        summary.UniqueBinariesUploaded.ShouldBe(1); // one additional binary uploaded
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(0); // pointer already existed
        summary.NewStateName.ShouldNotBeNull();

        var pfe = context2.StateRepository.GetPointerFileEntry(ToRelativePointerPath(filePath), includeBinaryProperties: true);
        pfe.ShouldNotBeNull();
        pfe.Hash.ShouldBe(modifiedFile.OriginalHash);
        pfe.BinaryProperties.ShouldNotBeNull();
        pfe.BinaryProperties.OriginalSize.ShouldBe(4000);

        //      The BinaryProperties of the originalFile are still present
        var originalBinaryProperties = context2.StateRepository.GetBinaryProperty(originalFile.OriginalHash);
        originalBinaryProperties.ShouldNotBeNull();

        //      The old Binary is still present
        storageBuilder.StoredChunks.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Incremental_NoChanges_ShouldSkipStateUploadAndDeleteLocalState()
    {
        // Arrange
        var baselineFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "docs" / "baseline.txt")
            .WithRandomContent(2048, seed: 5001)
            .Build();

        var storageBuilder = new MockArchiveStorageBuilder(fixture);

        var (_, initialContext, _, _) = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var firstResult = await CreateHandler().Handle(initialContext, CancellationToken.None);
        firstResult.IsSuccess.ShouldBeTrue();

        var (_, incrementalContext, _, _)             = await CreateHandlerContextAsync(storageBuilder: storageBuilder);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(incrementalContext);

        // Act
        var incrementalResult = await CreateHandler().Handle(incrementalContext, CancellationToken.None);

        // Assert
        incrementalResult.IsSuccess.ShouldBeTrue();
        var summary = incrementalResult.Value;

        summary.TotalLocalFiles.ShouldBe(expectedFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(0);
        summary.UniqueChunksUploaded.ShouldBe(0);
        summary.PointerFilesCreated.ShouldBe(0);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.NewStateName.ShouldBeNull();

        storageBuilder.UploadedStates.Count.ShouldBe(1);
        File.Exists(incrementalContext.StateRepository.StateDatabaseFile.FullName).ShouldBeFalse();
    }

    [Fact]
    public async Task Error_CancellationByUser_ShouldReturnFailureResult()
    {
        // Arrange
        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "cancel" / "large1.bin")
            .WithRandomContent(4096, seed: 1)
            .Build();

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "cancel" / "large2.bin")
            .WithRandomContent(4096, seed: 2)
            .Build();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await CreateHandler().Handle(handlerContext, cts.Token);

        // Assert
        result.IsFailed.ShouldBeTrue();
        result.Errors.ShouldNotBeEmpty();
        result.Errors.First().Message.ShouldContain("cancelled by user");

        storageBuilder.StoredChunks.Count.ShouldBe(0);
    }

    [Fact(Skip = "TODO")]
    public async Task Error_IndexTaskFails_ShouldSkipProblematicFileAndContinue()
    {
        // See example Error_HashTaskFails_ShouldSkipProblematicFileAndContinue
    }

    [Fact]
    public async Task Error_HashTaskFails_ShouldSkipProblematicFileAndContinue()
    {
        // Arrange
        var failingFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "hash" / "will-fail.bin")
            .WithRandomContent(1024, seed: 1)
            .Build();

        var successfulFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "hash" / "will-upload.bin")
            .WithRandomContent(1024, seed: 2)
            .Build();

        var deleted = false;
        var progressUpdates = new List<ProgressUpdate>();
        void ProgressHandler(ProgressUpdate update)
        {
            progressUpdates.Add(update);

            // Simulate a failure during hashing by deleting the file when we get to that point
            if (!deleted && update is FileProgressUpdate fileUpdate &&
                fileUpdate.FileName.EndsWith("will-fail.bin", StringComparison.OrdinalIgnoreCase) &&
                fileUpdate.StatusMessage?.Contains("Hashing", StringComparison.OrdinalIgnoreCase) == true)
            {
                deleted = true;
                failingFile.FilePair.BinaryFile.Delete();
            }
        }

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync(builder => builder.WithProgressReporter(new Progress<ProgressUpdate>(ProgressHandler)));

        var (expectedInitialFileCount, existingPointerCount) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(1);
        summary.BytesUploadedUncompressed.ShouldBe(successfulFile.OriginalContent.Length);

        //      The Binary & Pointer do not exist
        File.Exists(failingFile.FilePair.BinaryFile.FullName).ShouldBeFalse();
        File.Exists(failingFile.FilePair.PointerFile.FullName).ShouldBeFalse();

        storageBuilder.StoredChunks.Count.ShouldBe(1); // only 1 chunk stored (not 2)

        progressUpdates.OfType<FileProgressUpdate>()
            .Any(p => p.FileName.EndsWith("will-fail.bin", StringComparison.OrdinalIgnoreCase) && p.StatusMessage?.Contains("Error: BinaryFile does not exist", StringComparison.OrdinalIgnoreCase) == true)
            .ShouldBeTrue();

        summary.FilesSkipped.ShouldBe(1);
        summary.Warnings.ShouldContain(w => w.StartsWith("Error when hashing file '/hash/will-fail.bin': BinaryFile does not exist"));
    }

    [Fact]
    public async Task Error_UploadTaskFails_ShouldReturnFailure()
    {
        // Arrange
        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "uploads" / "large.bin")
            .WithRandomContent(4096, seed: 6201)
            .Build();

        var storageBuilder = new MockArchiveStorageBuilder(fixture)
            .WithThrowOnWrite(failureCount: 1);

        var (_, handlerContext, _, _) = await CreateHandlerContextAsync(storageBuilder: storageBuilder);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsFailed.ShouldBeTrue();
        result.Errors.First().Message.ShouldContain("failed");
        storageBuilder.StoredChunks.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Error_MultipleTasksFail_ShouldReturnAggregateException()
    {
        // Arrange
        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "multi" / "large.bin")
            .WithRandomContent(4096, seed: 6301)
            .Build();

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "multi" / "small.txt")
            .WithRandomContent(256, seed: 6302)
            .Build();

        var storageBuilder = new MockArchiveStorageBuilder(fixture)
            .WithThrowOnWrite(failureCount: 2);

        var (_, handlerContext, _, _) = await CreateHandlerContextAsync(storageBuilder: storageBuilder);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsFailed.ShouldBeTrue();
        result.Errors.First().Message.ShouldContain("multiple tasks failed");
        storageBuilder.StoredChunks.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Error_PointerFileOnly_ShouldReportWarningAndSkip()
    {
        // Arrange
        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.PointerFileOnly, UPath.Root / "orphans" / "lonely.bin")
            .Build();

        var progressUpdates = new List<ProgressUpdate>();
        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync(builder => builder.WithProgressReporter(new Progress<ProgressUpdate>(progressUpdates.Add)));

        var (expectedInitialFileCount, existingPointerCount) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(0);
        summary.PointerFilesCreated.ShouldBe(0);

        storageBuilder.StoredChunks.Count.ShouldBe(0);

        handlerContext.StateRepository.GetPointerFileEntry("/orphans/lonely.bin.pointer.arius", includeBinaryProperties: true)
            .ShouldBeNull();

        progressUpdates.OfType<FileProgressUpdate>()
            .Any(p => p.FileName.EndsWith("lonely.bin", StringComparison.OrdinalIgnoreCase) && p.StatusMessage?.Contains("pointer file without binary", StringComparison.OrdinalIgnoreCase) == true)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task StalePointerEntries_ShouldBeRemovedWhenMissingOnDisk()
    {
        // Arrange
        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "active.txt")
            .WithRandomContent(256, seed: 7)
            .Build();

        var (_, handlerContext, storageBuilder, _) = await CreateHandlerContextAsync();

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        var staleHash = FakeHashBuilder.GenerateValidHash(99);
        handlerContext.StateRepository.AddBinaryProperties(new BinaryProperties
        {
            Hash = staleHash,
            OriginalSize = 1,
            ArchivedSize = 1,
            StorageTier = StorageTier.Cool
        });
        handlerContext.StateRepository.UpsertPointerFileEntries(new PointerFileEntry
        {
            Hash = staleHash,
            RelativeName = "/stale.bin.pointer.arius",
            CreationTimeUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow
        });

        // Act
        var result = await CreateHandler().Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.PointerFileEntriesDeleted.ShouldBe(1);
        handlerContext.StateRepository.GetPointerFileEntry("/stale.bin.pointer.arius")
            .ShouldBeNull();

        storageBuilder.StoredChunks.Count.ShouldBe(1);
        storageBuilder.UploadedStates.ShouldNotBeEmpty();

        File.Exists(Path.Combine(fixture.TestRunSourceFolder.FullName, "active.txt.pointer.arius")).ShouldBeTrue();
    }
}
