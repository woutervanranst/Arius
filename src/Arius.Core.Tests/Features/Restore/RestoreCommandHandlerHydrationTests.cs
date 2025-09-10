using Arius.Core.Features.Restore;
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
    public async Task GetChunkStreamAsync_OnlineTier_IsSuccessPath()
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddChunks_BinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent, StorageTier.Hot)
            .Build();

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(BINARY.OriginalHash, BINARY.OriginalContent.Length, archivedSize: 10, StorageTier.Hot, pfes => { pfes.WithPointerFileEntry(BINARY.OriginalPath); })
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
        await storageMock.Received(1).OpenReadChunkAsync(BINARY.OriginalHash, Arg.Any<CancellationToken>());
        await storageMock.DidNotReceiveWithAnyArgs().OpenReadHydratedChunkAsync(default, default);
        // -- We logged it correctly
        fakeLoggerFactory
            .GetLogRecordByTemplate("Reading from hydrated blob {BlobName} for '{RelativeName}'.")
            .ShouldNotBeNull();
        // -- The rehydration question handler was NOT called
        rehydrationQuestionHandlerMock.Received(0)(Arg.Any<IReadOnlyList<RehydrationDetail>>());
        // -- The command result shows that no binaries are rehydrating
        result.Rehydrating.ShouldBeEmpty();
        // -- We did NOT start the rehydration
        await storageMock.DidNotReceiveWithAnyArgs().StartRehydrationAsync(default);
        // -- The Binary is successfully restored
        BINARY.FilePair.BinaryFile.ReadAllBytes().ShouldBe(BINARY.OriginalContent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetChunkStreamAsync_OnlineTier_BlobArchivedErrorPath(bool rehydrate)
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddChunks_BinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent, StorageTier.Archive /* ! Notice the mismatch with (2) */)
            .Build();

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(BINARY.OriginalHash, BINARY.OriginalContent.Length, archivedSize: 10, StorageTier.Hot /* ! Notice the mismatch with (1) */ , pfes => { pfes.WithPointerFileEntry(BINARY.OriginalPath); })
            .BuildFake();

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, bool>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(rehydrate);

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
        await storageMock.Received(1).OpenReadChunkAsync(BINARY.OriginalHash, Arg.Any<CancellationToken>());
        await storageMock.DidNotReceiveWithAnyArgs().OpenReadHydratedChunkAsync(default, default);
        // -- We logged it correctly
        fakeLoggerFactory
            .GetLogRecordByTemplate("Blob {BlobName} for '{RelativeName}' is unexpectedly in the Archive tier. Updating StateDatabase & added to the rehydration list.")
            .ShouldNotBeNull();
        // -- The StateDatabase is updated with the correct tier
        sr.GetBinaryProperty(BINARY.OriginalHash).StorageTier.ShouldBe(StorageTier.Archive);
        // -- The rehydration question handler was called with the correct file
        rehydrationQuestionHandlerMock.Received(1)(Arg.Is<IReadOnlyList<RehydrationDetail>>(list =>
            list.Any(d => d.RelativeName == BINARY.FilePair.FullName)));
        if (rehydrate)
        {
            // -- The command result shows that this binary is rehydrating
            result.Rehydrating.ShouldContain(d => d.RelativeName == BINARY.FilePair.FullName);
            // -- We started the rehydration
            await storageMock.Received(1).StartRehydrationAsync(BINARY.OriginalHash);
        }
        else
        {
            // -- The command result shows that no binaries are rehydrating
            result.Rehydrating.ShouldBeEmpty();
            await storageMock.DidNotReceiveWithAnyArgs().StartRehydrationAsync(default);
        }
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChunkStreamAsync_OnlineTier_BlobRehydratingErrorPath()
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            // ! Add a rehydrating chunk
            .AddChunks_Rehydrating_BinaryChunk(BINARY.OriginalHash)
            .Build();

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(BINARY.OriginalHash, BINARY.OriginalContent.Length, archivedSize: 10, StorageTier.Hot, pfes => { pfes.WithPointerFileEntry(BINARY.OriginalPath); })
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
        await storageMock.Received(1).OpenReadChunkAsync(BINARY.OriginalHash, Arg.Any<CancellationToken>());
        await storageMock.DidNotReceiveWithAnyArgs().OpenReadHydratedChunkAsync(default, default);
        // -- We logged it correctly
        fakeLoggerFactory
            .GetLogRecordByTemplate("Blob {BlobName} for '{RelativeName}' is still rehydrating. Try again later.")
            .ShouldNotBeNull();
        // -- The rehydration question handler NOT called
        rehydrationQuestionHandlerMock.Received(0)(Arg.Any<IReadOnlyList<RehydrationDetail>>());
        // -- The command result shows that this binary is rehydrating
        result.Rehydrating.ShouldContain(d => d.RelativeName == BINARY.FilePair.FullName);
        // -- Rehydration not started - already ongoing
        await storageMock.ReceivedWithAnyArgs(0).StartRehydrationAsync(default);
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChunkStreamAsync_OnlineTier_BlobNotFoundErrorPath()
    {

    }





    [Fact]
    public async Task GetChunkStreamAsync_OfflineTier_IsSuccessPath()
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddChunks_BinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent, StorageTier.Archive)
            // ! There is a hydrated binary chunk
            .AddChunksRehydrated_BinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent)
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
        // -- The command result shows that no binaries are rehydrating
        result.Rehydrating.ShouldBeEmpty();
        // -- We did NOT start the rehydration
        await storageMock.DidNotReceiveWithAnyArgs().StartRehydrationAsync(default);
        // -- The Binary is successfully restored
        BINARY.FilePair.BinaryFile.ReadAllBytes().ShouldBe(BINARY.OriginalContent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetChunkStreamAsync_OfflineTier_BlobNotFoundErrorPath(bool rehydrate)
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddChunks_BinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent, StorageTier.Archive)
            // ! There is NO hydrated binary chunk
            //.AddHydratedBinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent)
            .Build();

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(BINARY.OriginalHash, BINARY.OriginalContent.Length, archivedSize: 10, StorageTier.Archive, pfes => { pfes.WithPointerFileEntry(BINARY.OriginalPath); })
            .BuildFake();

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, bool>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(rehydrate);

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
        if (rehydrate)
        {
            // -- The command result shows that this binary is rehydrating
            result.Rehydrating.ShouldContain(d => d.RelativeName == BINARY.FilePair.FullName);
            // -- We started the rehydration
            await storageMock.Received(1).StartRehydrationAsync(BINARY.OriginalHash);
        }
        else
        {
            // -- The command result shows that no binaries are rehydrating
            result.Rehydrating.ShouldBeEmpty();
            await storageMock.DidNotReceiveWithAnyArgs().StartRehydrationAsync(default);
        }
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChunkStreamAsync_OfflineTier_BlobRehydratingErrorPath()
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddChunks_BinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent, StorageTier.Archive)
            // ! Add the Hydrating Binary Chunk
            .AddChunksRehydrated_Rehydrating_BinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent)
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
            .GetLogRecordByTemplate("Blob {BlobName} for '{RelativeName}' is still rehydrating. Try again later.")
            .ShouldNotBeNull();
        // -- The rehydration question handler NOT called
        rehydrationQuestionHandlerMock.Received(0)(Arg.Any<IReadOnlyList<RehydrationDetail>>());
        // -- The command result shows that this binary is rehydrating
        result.Rehydrating.ShouldContain(d => d.RelativeName == BINARY.FilePair.FullName);
        // -- Rehydration not started - already ongoing
        await storageMock.ReceivedWithAnyArgs(0).StartRehydrationAsync(default);
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChunkStreamAsync_OfflineTier_BlobArchivedErrorPath()
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddChunks_BinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent, StorageTier.Archive)
            // ! Edge case: add an archived chunk in the chunks-hydrated folder
            .AddChunksRehydrated_InArchiveTier_BinaryChunk(BINARY.OriginalHash, BINARY.OriginalContent)
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
            .GetLogRecordByTemplate("Blob {BlobName} for '{RelativeName}' is unexpectedly in the Archive tier. Hydrating it.")
            .ShouldNotBeNull();
        // -- The command result shows that this binary is rehydrating
        result.Rehydrating.ShouldContain(d => d.RelativeName == BINARY.FilePair.FullName);
        // -- Rehydration started without asking
        await storageMock.Received(1).StartRehydrationAsync(BINARY.OriginalHash);
        rehydrationQuestionHandlerMock.Received(0)(Arg.Any<IReadOnlyList<RehydrationDetail>>());
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }













}