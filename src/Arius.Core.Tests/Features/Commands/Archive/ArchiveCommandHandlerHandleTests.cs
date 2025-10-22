using Arius.Core.Features.Commands.Archive;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Arius.Core.Tests.Helpers.FakeLogger;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zio;

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

    private const int DefaultSmallFileBoundary = 1024;

    private static string ToRelativePointerPath(UPath binaryPath) => binaryPath.GetPointerFilePath().ToString();

    private static string ToAbsolutePointerPath(FixtureWithFileSystem fixture, UPath binaryPath) =>
        Path.Combine(fixture.TestRunSourceFolder.FullName, binaryPath.GetPointerFilePath().ToString().TrimStart('/'));

    private static void ResetTestRoot(FixtureWithFileSystem fixture, string containerName)
    {
        foreach (var directory in Directory.EnumerateDirectories(fixture.TestRunSourceFolder.FullName))
            Directory.Delete(directory, recursive: true);

        foreach (var file in Directory.EnumerateFiles(fixture.TestRunSourceFolder.FullName))
            File.Delete(file);

        var stateCacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Arius",
            "statecache",
            fixture.RepositoryOptions?.AccountName ?? "testaccount",
            containerName);

        if (Directory.Exists(stateCacheRoot))
            Directory.Delete(stateCacheRoot, recursive: true);
    }

    private static async Task<HandlerContext> BuildHandlerContextAsync(
        ArchiveCommand command,
        FakeArchiveStorage archiveStorage,
        FakeLoggerFactory loggerFactory) =>
        await new HandlerContextBuilder(command, loggerFactory)
            .WithArchiveStorage(archiveStorage)
            .BuildAsync();

    private static (int FileCount, int ExistingPointerCount) GetInitialFileStatistics(HandlerContext handlerContext)
    {
        var entries = handlerContext.FileSystem
            .EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories)
            .Select(FilePair.FromBinaryFileFileEntry)
            .ToList();

        var existingPointerFileCount = entries.Count(fp => fp.PointerFile.Exists);

        return (entries.Count, existingPointerFileCount);
    }

    private async Task<(ArchiveCommand Command, HandlerContext Context, FakeArchiveStorage Storage, FakeLoggerFactory LoggerFactory)>
        CreateHandlerContextAsync(
            string containerName,
            Action<ArchiveCommandBuilder>? configureCommand = null,
            FakeArchiveStorage? archiveStorage = null,
            FakeLoggerFactory? loggerFactory = null)
    {
        archiveStorage ??= new FakeArchiveStorage();
        loggerFactory  ??= new FakeLoggerFactory();

        var commandBuilder = new ArchiveCommandBuilder(fixture)
            .WithContainerName(containerName)
            .WithSmallFileBoundary(DefaultSmallFileBoundary)
            .WithHashingParallelism(1)
            .WithUploadParallelism(1);

        configureCommand?.Invoke(commandBuilder);

        var command        = commandBuilder.Build();
        var handlerContext = await BuildHandlerContextAsync(command, archiveStorage, loggerFactory);

        return (command, handlerContext, archiveStorage, loggerFactory);
    }

    [Fact]
    public async Task Single_LargeFile_FirstUpload_ShouldUploadBinaryAndPointer()
    {
        // Arrange
        var containerName = $"test-container-large-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var binaryPath = UPath.Root / "documents" / "presentation.pptx";
        var largeFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .WithRandomContent(4096, seed: 10)
            .Build();

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        var (expectedInitialFileCount, expectedExistingPointerFile) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

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

        archiveStorage.StoredChunks.Count.ShouldBe(1);
        var chunk = archiveStorage.StoredChunks.Single().Value;
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
        var containerName = $"test-container-small-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var binaryPath = UPath.Root / "notes" / "small.txt";
        var smallFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .WithRandomContent(512, seed: 2)
            .Build();

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        var (expectedInitialFileCount, expectedExistingPointerFile) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

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

        archiveStorage.StoredChunks.Count.ShouldBe(1);
        var tarChunk = archiveStorage.StoredChunks.Single().Value;
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
        var containerName = $"test-container-empty-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var binaryPath = UPath.Root / "empty" / "file.bin";
        var emptyFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .Build();

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        var (expectedInitialFileCount, expectedExistingPointerFile) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

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

        archiveStorage.StoredChunks.Count.ShouldBe(1);
        var tarChunk = archiveStorage.StoredChunks.Single().Value;
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
        var containerName = $"test-container-existing-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var binaryPath = UPath.Root / "existing" / "document.pdf";
        var binaryWithPointer = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .WithRandomContent(2048, seed: 5)
            .Build();

        var staleHash = FakeHashBuilder.GenerateValidHash(42);
        staleHash.ShouldNotBe(binaryWithPointer.OriginalHash);
        var stalePointer = binaryWithPointer.FilePair.CreatePointerFile(staleHash);
        stalePointer.ReadHash().ShouldBe(staleHash);

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        var (expectedInitialFileCount, expectedExistingPointerFile) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

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

        archiveStorage.StoredChunks.Count.ShouldBe(1);

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
    public async Task Multiple_AllUnique_MixedSizes_ShouldUploadLargeAndSmallBatches()
    {
        // Arrange
        var containerName = $"test-container-mixed-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

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

        var (command, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(
            containerName,
            builder => builder
                .WithProgressReporter(progressReporter)
                .WithHashingParallelism(1)
                .WithUploadParallelism(1)
                .WithSmallFileBoundary(DefaultSmallFileBoundary));

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

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
        var containerName = $"test-container-duplicates-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

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

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(2); // large owner + small owner
        summary.UniqueChunksUploaded.ShouldBe(2);   // large chunk + TAR
        summary.PointerFilesCreated.ShouldBe(4);
        summary.BytesUploadedUncompressed.ShouldBe(
            originalLargeFile.OriginalContent.Length + originalSmallFile.OriginalContent.Length);

        archiveStorage.StoredChunks.Count.ShouldBe(2);
        archiveStorage.StoredChunks.Values.Count(c => c.ContentType == "application/aes256cbc+gzip").ShouldBe(1);
        archiveStorage.StoredChunks.Values.Count(c => c.ContentType == "application/aes256cbc+tar+gzip").ShouldBe(1);

        var largeChunk = archiveStorage.StoredChunks.Values.Single(c => c.ContentType == "application/aes256cbc+gzip");
        largeChunk.Metadata.ShouldContainKey("OriginalContentLength");
        largeChunk.Metadata["OriginalContentLength"].ShouldBe(originalLargeFile.OriginalContent.Length.ToString());

        var tarChunk = archiveStorage.StoredChunks.Values.Single(c => c.ContentType == "application/aes256cbc+tar+gzip");
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
        var containerName = $"test-container-small-batch-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

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

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(paths.Length);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(paths.Length);
        summary.BytesUploadedUncompressed.ShouldBe(smallFiles.Sum(f => f.OriginalContent.Length));
        summary.PointerFileEntriesDeleted.ShouldBe(0);

        archiveStorage.StoredChunks.Count.ShouldBe(1);
        var tarChunk = archiveStorage.StoredChunks.Single().Value;
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
        // Arrange
        var containerName = $"test-container-multi-tar-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var paths = new[]
        {
            UPath.Root / "tar" / "alpha.bin",
            UPath.Root / "tar" / "beta.bin",
            UPath.Root / "tar" / "gamma.bin"
        };

        var sizes = new[] { 900, 900, 500 };

        var smallFiles = paths
            .Select((path, index) => new FakeFileBuilder(fixture)
                .WithActualFile(FilePairType.BinaryFileOnly, path)
                .WithRandomContent(sizes[index], seed: 200 + index)
                .Build())
            .ToArray();

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(paths.Length);
        summary.UniqueChunksUploaded.ShouldBe(2);
        summary.PointerFilesCreated.ShouldBe(paths.Length);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.BytesUploadedUncompressed.ShouldBe(smallFiles.Sum(f => f.OriginalContent.Length));

        var tarChunks = archiveStorage.StoredChunks.Values
            .Where(c => c.ContentType == "application/aes256cbc+tar+gzip")
            .ToList();
        tarChunks.Count.ShouldBe(2);
        tarChunks.Select(c => int.Parse(c.Metadata["SmallChunkCount"]))
            .OrderBy(v => v)
            .ShouldBe(new[] { 1, 2 });

        foreach (var path in paths)
        {
            File.Exists(ToAbsolutePointerPath(fixture, path)).ShouldBeTrue();
            handlerContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(path), includeBinaryProperties: true)
                .ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Multiple_SmallFiles_WithDuplicates_CrossTarBatches_ShouldWriteDeferredPointers()
    {
        // Arrange
        var containerName = $"test-container-small-duplicates-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var ownerAlpha = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "tar" / "alpha-owner.txt")
            .WithRandomContent(900, seed: 11)
            .Build();

        var ownerBeta = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "tar" / "beta-owner.txt")
            .WithRandomContent(920, seed: 12)
            .Build();

        _ = new FakeFileBuilder(fixture)
            .WithDuplicate(ownerAlpha, UPath.Root / "tar" / "gamma-duplicate.txt")
            .Build();

        var ownerOmega = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "tar" / "omega-owner.txt")
            .WithRandomContent(480, seed: 13)
            .Build();

        _ = new FakeFileBuilder(fixture)
            .WithDuplicate(ownerOmega, UPath.Root / "tar" / "zzz-duplicate.txt")
            .Build();

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.UniqueBinariesUploaded.ShouldBe(3);
        summary.UniqueChunksUploaded.ShouldBe(2);
        summary.PointerFilesCreated.ShouldBe(5);
        summary.PointerFileEntriesDeleted.ShouldBe(0);

        var tarChunks = archiveStorage.StoredChunks.Values
            .Where(c => c.ContentType == "application/aes256cbc+tar+gzip")
            .ToList();
        tarChunks.Count.ShouldBe(2);
        tarChunks.Select(c => int.Parse(c.Metadata["SmallChunkCount"]))
            .OrderBy(v => v)
            .ShouldBe(new[] { 1, 2 });

        var duplicatePaths = new[]
        {
            UPath.Root / "tar" / "gamma-duplicate.txt",
            UPath.Root / "tar" / "zzz-duplicate.txt"
        };

        foreach (var path in duplicatePaths)
        {
            File.Exists(ToAbsolutePointerPath(fixture, path)).ShouldBeTrue();
            handlerContext.StateRepository.GetPointerFileEntry(ToRelativePointerPath(path), includeBinaryProperties: true)
                .ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Incremental_AllFilesAlreadyUploaded_ShouldSkipUploads()
    {
        // Arrange
        var containerName = $"test-container-incremental-existing-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var binaryPath = UPath.Root / "incremental" / "presentation.pptx";
        var largeFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, binaryPath)
            .WithRandomContent(4096, seed: 501)
            .Build();

        var archiveStorage = new FakeArchiveStorage();

        var (_, initialContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var (initialFileCount, _) = GetInitialFileStatistics(initialContext);

        var firstResult = await handler.Handle(initialContext, CancellationToken.None);
        firstResult.IsSuccess.ShouldBeTrue();

        var originalHash = largeFile.OriginalHash;

        // Corrupt pointer file to ensure it is rewritten on incremental run
        var staleHash = FakeHashBuilder.GenerateValidHash(999);
        staleHash.ShouldNotBe(originalHash);
        largeFile.FilePair.CreatePointerFile(staleHash);

        var (_, incrementalContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(incrementalContext);

        // Act
        var incrementalResult = await handler.Handle(incrementalContext, CancellationToken.None);

        // Assert
        incrementalResult.IsSuccess.ShouldBeTrue();
        var summary = incrementalResult.Value;

        summary.TotalLocalFiles.ShouldBe(expectedFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(0);
        summary.UniqueChunksUploaded.ShouldBe(0);
        summary.PointerFilesCreated.ShouldBe(0);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.BytesUploadedUncompressed.ShouldBe(0);
        summary.NewStateName.ShouldBeNull();

        largeFile.FilePair.PointerFile.ReadHash().ShouldBe(originalHash);

        archiveStorage.StoredChunks.Count.ShouldBe(1);
        archiveStorage.UploadedStates.Count.ShouldBe(1);

        var pointerEntry = incrementalContext.StateRepository
            .GetPointerFileEntry(ToRelativePointerPath(binaryPath), includeBinaryProperties: true);
        pointerEntry.ShouldNotBeNull();
        pointerEntry!.Hash.ShouldBe(originalHash);
    }

    [Fact]
    public async Task Incremental_MixOfNewAndExisting_ShouldUploadOnlyNewFiles()
    {
        // Arrange
        var containerName = $"test-container-incremental-mixed-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var existingFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "docs" / "existing.pdf")
            .WithRandomContent(4096, seed: 2001)
            .Build();

        var archiveStorage = new FakeArchiveStorage();

        var (_, initialContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var firstResult = await handler.Handle(initialContext, CancellationToken.None);
        firstResult.IsSuccess.ShouldBeTrue();

        var newSmallFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "docs" / "new-note.txt")
            .WithRandomContent(512, seed: 2002)
            .Build();

        var (_, incrementalContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(incrementalContext);

        // Act
        var incrementalResult = await handler.Handle(incrementalContext, CancellationToken.None);

        // Assert
        incrementalResult.IsSuccess.ShouldBeTrue();
        var summary = incrementalResult.Value;

        summary.TotalLocalFiles.ShouldBe(expectedFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(1);
        summary.BytesUploadedUncompressed.ShouldBe(newSmallFile.OriginalContent.Length);
        summary.PointerFileEntriesDeleted.ShouldBe(0);
        summary.NewStateName.ShouldNotBeNull();

        archiveStorage.StoredChunks.Values.Count().ShouldBe(2);

        var pointerEntry = incrementalContext.StateRepository
            .GetPointerFileEntry(ToRelativePointerPath(newSmallFile.OriginalPath), includeBinaryProperties: true);
        pointerEntry.ShouldNotBeNull();
        pointerEntry!.BinaryProperties.ShouldNotBeNull();
        pointerEntry.BinaryProperties!.OriginalSize.ShouldBe(newSmallFile.OriginalContent.Length);
    }

    [Fact]
    public async Task Incremental_FileDeleted_PointerRemains_ShouldCleanUpStateEntry()
    {
        // Arrange
        var containerName = $"test-container-incremental-deleted-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var deletedFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "docs" / "to-delete.txt")
            .WithRandomContent(2048, seed: 3001)
            .Build();

        var archiveStorage = new FakeArchiveStorage();

        var (_, initialContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var firstResult = await handler.Handle(initialContext, CancellationToken.None);
        firstResult.IsSuccess.ShouldBeTrue();

        File.Delete(Path.Combine(fixture.TestRunSourceFolder.FullName, "docs", "to-delete.txt"));
        File.Delete(Path.Combine(fixture.TestRunSourceFolder.FullName, "docs", "to-delete.txt.pointer.arius"));

        var (_, incrementalContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(incrementalContext);

        // Act
        var incrementalResult = await handler.Handle(incrementalContext, CancellationToken.None);

        // Assert
        incrementalResult.IsSuccess.ShouldBeTrue();
        var summary = incrementalResult.Value;

        summary.TotalLocalFiles.ShouldBe(expectedFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(0);
        summary.PointerFileEntriesDeleted.ShouldBe(1);
        summary.NewStateName.ShouldNotBeNull();

        var pointerEntry = incrementalContext.StateRepository
            .GetPointerFileEntry("/docs/to-delete.txt.pointer.arius", includeBinaryProperties: true);
        pointerEntry.ShouldBeNull();

        archiveStorage.StoredChunks.Count.ShouldBe(1);
        archiveStorage.UploadedStates.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Incremental_FileModified_ShouldUploadNewHashAndPreserveOldBinaryProperties()
    {
        // Arrange
        var containerName = $"test-container-incremental-modified-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var filePath = UPath.Root / "docs" / "mutable.bin";
        var originalFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, filePath)
            .WithRandomContent(3072, seed: 4001)
            .Build();

        var archiveStorage = new FakeArchiveStorage();

        var (_, initialContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var firstResult = await handler.Handle(initialContext, CancellationToken.None);
        firstResult.IsSuccess.ShouldBeTrue();

        var originalHash = originalFile.OriginalHash;
        var originalBinaryProperties = initialContext.StateRepository.GetBinaryProperty(originalHash);
        originalBinaryProperties.ShouldNotBeNull();

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, filePath)
            .WithRandomContent(3584, seed: 4002)
            .Build();

        var (_, incrementalContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(incrementalContext);

        // Act
        var incrementalResult = await handler.Handle(incrementalContext, CancellationToken.None);

        // Assert
        incrementalResult.IsSuccess.ShouldBeTrue();
        var summary = incrementalResult.Value;

        summary.TotalLocalFiles.ShouldBe(expectedFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.UniqueChunksUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(0); // pointer already existed
        summary.NewStateName.ShouldNotBeNull();

        var pointerEntry = incrementalContext.StateRepository
            .GetPointerFileEntry(ToRelativePointerPath(filePath), includeBinaryProperties: true);
        pointerEntry.ShouldNotBeNull();
        pointerEntry!.Hash.ShouldNotBe(originalHash);
        pointerEntry.BinaryProperties.ShouldNotBeNull();

        var oldBinaryProperties = incrementalContext.StateRepository.GetBinaryProperty(originalHash);
        oldBinaryProperties.ShouldNotBeNull();

        archiveStorage.StoredChunks.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Incremental_NoChanges_ShouldSkipStateUploadAndDeleteLocalState()
    {
        // Arrange
        var containerName = $"test-container-incremental-nochanges-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var baselineFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "docs" / "baseline.txt")
            .WithRandomContent(2048, seed: 5001)
            .Build();

        var archiveStorage = new FakeArchiveStorage();

        var (_, initialContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var firstResult = await handler.Handle(initialContext, CancellationToken.None);
        firstResult.IsSuccess.ShouldBeTrue();

        var (_, incrementalContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);
        var (expectedFileCount, existingPointerCount) = GetInitialFileStatistics(incrementalContext);

        // Act
        var incrementalResult = await handler.Handle(incrementalContext, CancellationToken.None);

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

        archiveStorage.UploadedStates.Count.ShouldBe(1);
        File.Exists(incrementalContext.StateRepository.StateDatabaseFile.FullName).ShouldBeFalse();
    }

    [Fact]
    public async Task Error_CancellationByUser_ShouldReturnFailureResult()
    {
        // Arrange
        var containerName = $"test-container-error-cancel-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "cancel" / "large1.bin")
            .WithRandomContent(4096, seed: 6001)
            .Build();

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "cancel" / "large2.bin")
            .WithRandomContent(4096, seed: 6002)
            .Build();

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await handler.Handle(handlerContext, cts.Token);

        // Assert
        result.IsFailed.ShouldBeTrue();
        result.Errors.ShouldNotBeEmpty();
        result.Errors.First().Message.ShouldContain("cancelled by user");

        archiveStorage.StoredChunks.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Error_HashTaskFails_ShouldSkipProblematicFileAndContinue()
    {
        // Arrange
        var containerName = $"test-container-error-hash-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        var failingPath = UPath.Root / "hash" / "will-fail.bin";
        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, failingPath)
            .WithRandomContent(1024, seed: 6101)
            .Build();

        var successfulPath = UPath.Root / "hash" / "will-upload.bin";
        var successfulFile = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, successfulPath)
            .WithRandomContent(2048, seed: 6102)
            .Build();

        var failingAbsolutePath = Path.Combine(fixture.TestRunSourceFolder.FullName, "hash", "will-fail.bin");

        var deleted = false;
        var progressUpdates = new List<ProgressUpdate>();
        void HandleProgress(ProgressUpdate update)
        {
            progressUpdates.Add(update);
            if (!deleted && update is FileProgressUpdate fileUpdate &&
                fileUpdate.FileName.EndsWith("will-fail.bin", StringComparison.OrdinalIgnoreCase) &&
                fileUpdate.StatusMessage?.Contains("Hashing", StringComparison.OrdinalIgnoreCase) == true)
            {
                deleted = true;
                File.Delete(failingAbsolutePath);
            }
        }

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(
            containerName,
            builder => builder.WithProgressReporter(new Progress<ProgressUpdate>(HandleProgress)));

        var (expectedInitialFileCount, existingPointerCount) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(1);
        summary.PointerFilesCreated.ShouldBe(1);
        summary.BytesUploadedUncompressed.ShouldBe(successfulFile.OriginalContent.Length);

        File.Exists(failingAbsolutePath).ShouldBeFalse();
        File.Exists(Path.Combine(fixture.TestRunSourceFolder.FullName, "hash", "will-fail.bin.pointer.arius")).ShouldBeFalse();

        archiveStorage.StoredChunks.Count.ShouldBe(1);

        progressUpdates.OfType<FileProgressUpdate>()
            .Any(p => p.FileName.EndsWith("will-fail.bin", StringComparison.OrdinalIgnoreCase) && p.StatusMessage?.Contains("Error", StringComparison.OrdinalIgnoreCase) == true)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Error_UploadTaskFails_ShouldReturnFailure()
    {
        // Arrange
        var containerName = $"test-container-error-upload-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "uploads" / "large.bin")
            .WithRandomContent(4096, seed: 6201)
            .Build();

        var archiveStorage = new ThrowingArchiveStorage(failureCount: 1);

        var (_, handlerContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsFailed.ShouldBeTrue();
        result.Errors.First().Message.ShouldContain("failed");
        archiveStorage.StoredChunks.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Error_MultipleTasksFail_ShouldReturnAggregateException()
    {
        // Arrange
        var containerName = $"test-container-error-multi-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "multi" / "large.bin")
            .WithRandomContent(4096, seed: 6301)
            .Build();

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "multi" / "small.txt")
            .WithRandomContent(256, seed: 6302)
            .Build();

        var archiveStorage = new ThrowingArchiveStorage(failureCount: 2);

        var (_, handlerContext, _, _) = await CreateHandlerContextAsync(containerName, archiveStorage: archiveStorage);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsFailed.ShouldBeTrue();
        result.Errors.First().Message.ShouldContain("multiple tasks failed");
        archiveStorage.StoredChunks.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Error_PointerFileOnly_ShouldReportWarningAndSkip()
    {
        // Arrange
        var containerName = $"test-container-error-pointer-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.PointerFileOnly, UPath.Root / "orphans" / "lonely.bin")
            .Build();

        var progressUpdates = new List<ProgressUpdate>();
        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(
            containerName,
            builder => builder.WithProgressReporter(new Progress<ProgressUpdate>(progressUpdates.Add)));

        var (expectedInitialFileCount, existingPointerCount) = GetInitialFileStatistics(handlerContext);

        // Act
        var result = await handler.Handle(handlerContext, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;

        summary.TotalLocalFiles.ShouldBe(expectedInitialFileCount);
        summary.ExistingPointerFiles.ShouldBe(existingPointerCount);
        summary.UniqueBinariesUploaded.ShouldBe(0);
        summary.PointerFilesCreated.ShouldBe(0);

        archiveStorage.StoredChunks.Count.ShouldBe(0);

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
        var containerName = $"test-container-stale-{Guid.CreateVersion7()}";
        ResetTestRoot(fixture, containerName);

        _ = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, UPath.Root / "active.txt")
            .WithRandomContent(256, seed: 7)
            .Build();

        var (_, handlerContext, archiveStorage, _) = await CreateHandlerContextAsync(containerName);

        var (expectedInitialFileCount, _) = GetInitialFileStatistics(handlerContext);

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

    private sealed class ThrowingArchiveStorage : FakeArchiveStorage
    {
        private int remainingFailures;
        private readonly Func<Hash, bool>? predicate;

        public ThrowingArchiveStorage(int failureCount, Func<Hash, bool>? predicate = null)
        {
            remainingFailures = failureCount;
            this.predicate    = predicate;
        }

        public override Task<Result<Stream>> OpenWriteChunkAsync(
            Hash h,
            CompressionLevel compressionLevel,
            string contentType,
            IDictionary<string, string>? metadata = null,
            IProgress<long>? progress = null,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            if ((predicate is null || predicate(h)) && Interlocked.Decrement(ref remainingFailures) >= 0)
            {
                return Task.FromResult(Result.Fail<Stream>(new ExceptionalError(new IOException("Simulated upload failure"))));
            }

            return base.OpenWriteChunkAsync(h, compressionLevel, contentType, metadata, progress, overwrite, cancellationToken);
        }
    }
}
