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
        handler = new RestoreCommandHandler(fakeLoggerFactory.CreateLogger<RestoreCommandHandler>(), fakeLoggerFactory, fixture.AriusConfiguration);
    }


    // -- LARGE | ONLINE

    [Fact]
    public async Task GetChunkStreamAsyncForLargeFile_OnlineTier_IsSuccessPath()
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

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(RehydrationDecision.StandardPriority);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
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
        await storageMock.DidNotReceiveWithAnyArgs().StartHydrationAsync(default, default);
        // -- The Binary is successfully restored
        BINARY.FilePair.BinaryFile.ReadAllBytes().ShouldBe(BINARY.OriginalContent);
    }

    [Theory]
    [InlineData(RehydrationDecision.StandardPriority)]
    [InlineData(RehydrationDecision.DoNotRehydrate)]
    public async Task GetChunkStreamAsyncForLargeFile_OnlineTier_BlobArchivedErrorPath(RehydrationDecision rehydrate)
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
            .WithBinaryProperty(BINARY.OriginalHash, BINARY.OriginalContent.Length, archivedSize: 10, StorageTier.Hot /* ! Notice the mismatch with (1) */, pfes => { pfes.WithPointerFileEntry(BINARY.OriginalPath); })
            .BuildFake();

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(rehydrate);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
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
        if (rehydrate != RehydrationDecision.DoNotRehydrate)
        {
            // -- The command result shows that this binary is rehydrating
            result.Rehydrating.ShouldContain(d => d.RelativeName == BINARY.FilePair.FullName);
            // -- We started the rehydration
            await storageMock.Received(1).StartHydrationAsync(BINARY.OriginalHash, RehydratePriority.Standard);
        }
        else
        {
            // -- The command result shows that no binaries are rehydrating
            result.Rehydrating.ShouldBeEmpty();
            await storageMock.DidNotReceiveWithAnyArgs().StartHydrationAsync(default, default);
        }

        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChunkStreamAsyncForLargeFile_OnlineTier_BlobRehydratingErrorPath()
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

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(RehydrationDecision.StandardPriority);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
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
        await storageMock.ReceivedWithAnyArgs(0).StartHydrationAsync(default, default);
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChunkStreamAsyncForLargeFile_OnlineTier_BlobNotFoundErrorPath()
    {
        // Arrange
        var BINARY = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            // ! No chunk is added
            .Build();

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(BINARY.OriginalHash, BINARY.OriginalContent.Length, archivedSize: 10, StorageTier.Hot, pfes => { pfes.WithPointerFileEntry(BINARY.OriginalPath); })
            .BuildFake();

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(RehydrationDecision.StandardPriority);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
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
            .GetLogRecordByTemplate("Did not find blob {BlobName} for '{RelativeName}'. This binary is lost.")
            .ShouldNotBeNull();
        // -- The rehydration question handler NOT called
        rehydrationQuestionHandlerMock.Received(0)(Arg.Any<IReadOnlyList<RehydrationDetail>>());
        // -- The command result shows that no binaries are rehydrating
        result.Rehydrating.ShouldBeEmpty();
        // -- No rehydrations started
        await storageMock.ReceivedWithAnyArgs(0).StartHydrationAsync(default, default);
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }


    // -- LARGE | OFFLINE

    [Fact]
    public async Task GetChunkStreamAsyncForLargeFile_OfflineTier_IsSuccessPath()
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

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(RehydrationDecision.StandardPriority);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
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
        await storageMock.DidNotReceiveWithAnyArgs().StartHydrationAsync(default, default);
        // -- The Binary is successfully restored
        BINARY.FilePair.BinaryFile.ReadAllBytes().ShouldBe(BINARY.OriginalContent);
    }

    [Theory]
    [InlineData(RehydrationDecision.StandardPriority)]
    [InlineData(RehydrationDecision.DoNotRehydrate)]
    public async Task GetChunkStreamAsyncForLargeFile_OfflineTier_BlobNotFoundErrorPath(RehydrationDecision rehydrate)
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

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(rehydrate);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
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
        if (rehydrate != RehydrationDecision.DoNotRehydrate)
        {
            // -- The command result shows that this binary is rehydrating
            result.Rehydrating.ShouldContain(d => d.RelativeName == BINARY.FilePair.FullName);
            // -- We started the rehydration
            await storageMock.Received(1).StartHydrationAsync(BINARY.OriginalHash, RehydratePriority.Standard);
        }
        else
        {
            // -- The command result shows that no binaries are rehydrating
            result.Rehydrating.ShouldBeEmpty();
            await storageMock.DidNotReceiveWithAnyArgs().StartHydrationAsync(default, default);
        }

        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChunkStreamAsyncForLargeFile_OfflineTier_BlobRehydratingErrorPath()
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

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(RehydrationDecision.StandardPriority);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
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
        await storageMock.ReceivedWithAnyArgs(0).StartHydrationAsync(default, default);
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChunkStreamAsyncForLargeFile_OfflineTier_BlobArchivedErrorPath()
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

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(RehydrationDecision.StandardPriority);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
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
        await storageMock.Received(1).StartHydrationAsync(BINARY.OriginalHash, RehydratePriority.Standard);
        rehydrationQuestionHandlerMock.Received(0)(Arg.Any<IReadOnlyList<RehydrationDetail>>());
        // -- The Binary is not restored
        BINARY.FilePair.BinaryFile.Exists.ShouldBeFalse();
    }


    // -- SMALL | ONLINE

    [Fact(Skip = "Not yet implemented")]
    public async Task GetChunkStreamAsyncForSmallFile_OnlineTier_IsSuccessPath()
    {
    }

    [Fact(Skip = "Not yet implemented")]
    public async Task GetChunkStreamAsyncForSmallFile_OnlineTier_BlobArchivedErrorPath()
    {
    }

    [Fact(Skip = "Not yet implemented")]
    public async Task GetChunkStreamAsyncForSmallFile_OnlineTier_BlobRehydratingErrorPath()
    {
    }

    [Fact(Skip = "Not yet implemented")]
    public async Task GetChunkStreamAsyncForSmallFile_OnlineTier_BlobNotFoundErrorPath()
    {
    }


    // -- SMALL | OFFLINE

    [Fact]
    public async Task GetChunkStreamAsyncForSmallFile_OfflineTier_IsSuccessPath()
    {
        // Arrange
        var tarContent = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file.jpg")
            .WithRandomContent(10, 1)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddHydratedTarChunk(out var parentHash, t => { t.AddBinary(tarContent.OriginalHash, tarContent.OriginalContent); })
            .Build();

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(tarContent.OriginalHash, parentHash, tarContent.OriginalContent.Length, storageTier: StorageTier.Archive, pointerFileEntries: pfes => { pfes.WithPointerFileEntry(tarContent.OriginalPath); })
            .BuildFake();

        var rehydrationQuestionHandlerMock = Substitute.For<Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision>>();
        rehydrationQuestionHandlerMock(Arg.Any<IReadOnlyList<RehydrationDetail>>()).Returns(RehydrationDecision.StandardPriority);

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets("./")
            .WithRehydrationQuestionHandler(rehydrationQuestionHandlerMock)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
            .WithArchiveStorage(storageMock)
            .WithStateRepository(sr)
            .WithBaseFileSystem(fixture.FileSystem)
            .BuildAsync();

        // Act
        var result = await handler.Handle(hc, CancellationToken.None);

        // Assert

        // -- We read from the hydrated chunks
        await storageMock.DidNotReceiveWithAnyArgs().OpenReadChunkAsync(default, default);
        await storageMock.Received(1).OpenReadHydratedChunkAsync(parentHash, Arg.Any<CancellationToken>());
        // -- We logged it correctly
        fakeLoggerFactory
            .GetLogRecordByTemplate("Reading from rehydrated blob {BlobName} for '{RelativeName}'.")
            .ShouldNotBeNull();
        // -- The rehydration question handler was NOT called
        rehydrationQuestionHandlerMock.Received(0)(Arg.Any<IReadOnlyList<RehydrationDetail>>());
        // -- The command result shows that no binaries are rehydrating
        result.Rehydrating.ShouldBeEmpty();
        // -- We did NOT start the rehydration
        await storageMock.DidNotReceiveWithAnyArgs().StartHydrationAsync(default, default);
        // -- The Binary is successfully restored
        tarContent.FilePair.BinaryFile.ReadAllBytes().ShouldBe(tarContent.OriginalContent);
    }

    [Fact(Skip = "Not yet implemented")]
    public async Task GetChunkStreamAsyncForSmallFile_OfflineTier_BlobNotFoundErrorPath()
    {
    }

    [Fact(Skip = "Not yet implemented")]

    public async Task GetChunkStreamAsyncForSmallFile_OfflineTier_BlobRehydratingErrorPath()
    {
    }

    [Fact(Skip = "Not yet implemented")]
    public async Task GetChunkStreamAsyncForSmallFile_OfflineTier_BlobArchivedErrorPath()
    {
    }
}