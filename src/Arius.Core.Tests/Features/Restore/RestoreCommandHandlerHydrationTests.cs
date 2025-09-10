using Arius.Core.Features.Restore;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.FakeLogger;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using NSubstitute;
using Shouldly;

namespace Arius.Core.Tests.Features.Restore;

public class RestoreCommandHandlerHydrationTests
{
    private readonly FixtureWithFileSystem fixture;
    private readonly FakeLoggerFactory     fakeLoggerFactory = new();
    private readonly RestoreCommandHandler handler;

    public RestoreCommandHandlerHydrationTests()
    {
        fixture = new();
        handler      = new RestoreCommandHandler(fakeLoggerFactory.CreateLogger<RestoreCommandHandler>(), fakeLoggerFactory, fixture.AriusConfiguration);
    }


    [Fact]
    public async Task GetChunkStreamAsync_Online_Success()
    {

    }

    [Fact]
    public async Task GetChunkStreamAsync_Online_BlobArchivedError()
    {

    }

    [Fact]
    public async Task GetChunkStreamAsync_Online_BlobRehydratingError()
    {

    }

    [Fact]
    public async Task GetChunkStreamAsync_Online_BlobNotFoundError()
    {

    }

    [Fact]
    public async Task GetChunkStreamAsync_InArchive_ButHydrated_UsesHydratedAndRestored() // OK
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddBinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent, StorageTier.Archive)
            // -- There is a hydrated binary chunk
            .AddHydratedBinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent)
            .Build();

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(BINARY.OriginalHash, BINARY.OriginalContent.Length, archivedSize: 10, StorageTier.Archive, pfes => { pfes.WithPointerFileEntry(BINARY.OriginalPath); })
            .BuildFake();

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, bool>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(true);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithIncludePointers(true)
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command)
            .WithArchiveStorage(storageMock)
            .WithStateRepository(sr)
            .WithBaseFileSystem(fixture.FileSystem)
            .BuildAsync();

        // Act
        var result = await handler.Handle(hc, CancellationToken.None);

        // Assert
        // -- We read from the hydrated chunks
        await storageMock.DidNotReceiveWithAnyArgs().OpenReadChunkAsync(default, default);
        await storageMock.Received(1).OpenReadHydratedChunkAsync(BINARY.OriginalHash, Arg.Any<CancellationToken>());
        // -- We logged it correctly
        fakeLoggerFactory
            .GetLogRecordByTemplate("Reading from rehydrated blob {BlobName} for '{RelativeName}'.")
            .ShouldNotBeNull();
        // -- The rehydration question handler was NOT called
        rehydrationQuestionHandlerMock.Received(0)(Arg.Any<IReadOnlyList<RehydrationDetail>>());
        // -- It is in the result
        result.Rehydrating.ShouldBeEmpty();
        // -- The Binary is successfully restored
        BINARY.FilePair.BinaryFile.ReadAllBytes().ShouldBe(BINARY.OriginalContent);
    }

    [Fact]
    public async Task GetChunkStreamAsync_InArchive_HydrationStarted()
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddBinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent, StorageTier.Archive)
            // -- There is NO hydrated binary chunk
            //.AddHydratedBinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent)
            .Build();

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(BINARY.OriginalHash, BINARY.OriginalContent.Length, archivedSize: 10, StorageTier.Archive, pfes => { pfes.WithPointerFileEntry(BINARY.OriginalPath); })
            .BuildFake();

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, bool>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(true);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithIncludePointers(true)
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command)
            .WithArchiveStorage(storageMock)
            .WithStateRepository(sr)
            .WithBaseFileSystem(fixture.FileSystem)
            .BuildAsync();

        // Act
        var result = await handler.Handle(hc, CancellationToken.None);

        // Assert
        // -- We read from the hydrated chunks
        await storageMock.DidNotReceiveWithAnyArgs().OpenReadChunkAsync(default, default);
        await storageMock.Received(1).OpenReadHydratedChunkAsync(BINARY.OriginalHash, Arg.Any<CancellationToken>());
        // -- We logged it correctly
        fakeLoggerFactory
            .GetLogRecordByTemplate("Blob {BlobName} for '{RelativeName}' is in the Archive tier. Added to the rehydration list.")
            .ShouldNotBeNull();
        // -- The rehydration question handler was called with the correct file
        rehydrationQuestionHandlerMock.Received(1)(Arg.Is<IReadOnlyList<RehydrationDetail>>(list =>
            list.Any(d => d.RelativeName == BINARY.FilePair.FullName)));
        // -- It is in the result
        result.Rehydrating.ShouldContain(d => d.RelativeName == BINARY.FilePair.FullName);
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChunkStreamAsync_InArchive_BlobRehydratingError()
    {

    }

    [Fact]
    public async Task GetChunkStreamAsync_InArchive_BlobNotFoundError_ShouldStartRehydration()
    {
    }












    //[Fact]
    //public async Task Restore_HotBlob_UsesRegularChunkAsync()
    //{
    //    // Arrange
    //    var hotFile = new FakeFileBuilder(fixture)
    //        .WithNonExistingFile("/hot-file.jpg")
    //        .WithRandomContent(10, 1)
    //        .Build();

    //    var storageMock = new MockArchiveStorageBuilder(fixture)
    //        .AddBinaryChunk(hotFile.OriginalHash, hotFile.OriginalContent)
    //        .Build();

    //    var sr = new StateRepositoryBuilder()
    //        .WithBinaryProperty(hotFile.OriginalHash, hotFile.OriginalContent.Length, storageTier: StorageTier.Hot, pointerFileEntries: pfes =>
    //        {
    //            pfes.WithPointerFileEntry(hotFile.OriginalPath);
    //        })
    //        .BuildFake();

    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets($".{hotFile.OriginalPath}")
    //        .Build();

    //    var hc = await new HandlerContextBuilder(command)
    //        .WithArchiveStorage(storageMock)
    //        .WithStateRepository(sr)
    //        .WithBaseFileSystem(fixture.FileSystem)
    //        .BuildAsync();

    //    // Act
    //    await handler.Handle(hc, CancellationToken.None);

    //    // Assert
    //    await storageMock.Received(1).OpenReadChunkAsync(hotFile.OriginalHash, Arg.Any<CancellationToken>());
    //    await storageMock.DidNotReceive().OpenReadHydratedChunkAsync(hotFile.OriginalHash, Arg.Any<CancellationToken>());
    //    hotFile.FilePair.BinaryFile.ReadAllBytes().ShouldBe(hotFile.OriginalContent);
    //}

    //[Fact]
    //public async Task Restore_ArchivedTarBlob_UsesHydratedChunkAsync()
    //{
    //    // Arrange
    //    var tarContent = new FakeFileBuilder(fixture)
    //        .WithActualFile(FilePairType.None, "/tar-content.jpg")
    //        .WithRandomContent(10, 1)
    //        .Build();

    //    var storageMock = new MockArchiveStorageBuilder(fixture)
    //        .AddHydratedTarChunk(out var tarHash, t =>
    //        {
    //            t.AddBinary(tarContent.OriginalHash, tarContent.OriginalContent);
    //        })
    //        .Build();

    //    var sr = new StateRepositoryBuilder()
    //        .WithBinaryProperty(tarContent.OriginalHash, tarHash, tarContent.OriginalContent.Length, storageTier: StorageTier.Archive, pointerFileEntries: pfes =>
    //        {
    //            pfes.WithPointerFileEntry(tarContent.OriginalPath);
    //        })
    //        .BuildFake();

    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets($".{tarContent.OriginalPath}")
    //        .Build();

    //    var hc = await new HandlerContextBuilder(command)
    //        .WithArchiveStorage(storageMock)
    //        .WithStateRepository(sr)
    //        .WithBaseFileSystem(fixture.FileSystem)
    //        .BuildAsync();

    //    // Act
    //    await handler.Handle(hc, CancellationToken.None);

    //    // Assert
    //    await storageMock.Received(1).OpenReadHydratedChunkAsync(tarHash, Arg.Any<CancellationToken>());
    //    await storageMock.DidNotReceive().OpenReadChunkAsync(tarHash, Arg.Any<CancellationToken>());
    //    tarContent.FilePair.BinaryFile.ReadAllBytes().ShouldBe(tarContent.OriginalContent);
    //}



}