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

public class RestoreCommandHandlerInMemoryTests : IClassFixture<Fixture>
{
    private readonly Fixture               fixture;
    private readonly FakeLoggerFactory     fakeLoggerFactory = new();
    private readonly RestoreCommandHandler handler;

    public RestoreCommandHandlerInMemoryTests(Fixture fixture)
    {
        this.fixture  = fixture;
        handler       = new RestoreCommandHandler(fakeLoggerFactory.CreateLogger<RestoreCommandHandler>(), fakeLoggerFactory, fixture.AriusConfiguration);
    }

    [Fact]
    public async Task Restore_Mocked_HappyPath()
    {
        // Arrange
        var NOTEXISTINGFILE = new FakeFileBuilder(fixture)
            .WithNonExistingFile("/file1.jpg")
            .WithRandomContent(10, 1)
            .WithCreationTimeUtc("25/05/2017 06:00:00")
            .WithLastWriteTimeUtc("25/05/2017 07:00:00")
            .Build();

        var DUPLICATEBINARIES = new FakeFileBuilder(fixture)
            .WithNonExistingFiles("/Sam/file2.jpg", "/Sam/file2-duplicate.jpg")
            .WithRandomContent(10, 2)
            .BuildMany();
        Assert.Equal(DUPLICATEBINARIES[0].OriginalContent, DUPLICATEBINARIES[1].OriginalContent);

        var EXISTINGFILE = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, "/Sam/file3.jpg")
            .WithRandomContent(1, 3)
            .WithCreationTimeUtc(StateRepositoryBuilder.DEFAULTUTCTIME)
            .WithLastWriteTimeUtc(StateRepositoryBuilder.DEFAULTUTCTIME)
            .Build();
        
        var EXISTINGFILEWITHWRONGHASH = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, "/Sam/file4.jpg")
            .WithRandomContent(1, 4)
            .Build();
        EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.WriteAllText("This file was overwritten");

        var TARCONTENT1 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.None, "/Sam/file5.jpg")
            .WithRandomContent(10, 5)
            .Build();
        var TARCONTENT2 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.None, "/Sam/file6.jpg")
            .WithRandomContent(10, 6)
            .Build();
        var TARCONTENT3 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.None, "/Sam/file7.jpg")
            .WithRandomContent(10, 7)
            .Build();

        
        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddBinaryChunk(NOTEXISTINGFILE.OriginalHash, NOTEXISTINGFILE.OriginalContent)
            .AddBinaryChunk(DUPLICATEBINARIES[0].OriginalHash, DUPLICATEBINARIES[1].OriginalContent)
            .AddBinaryChunk(EXISTINGFILE.OriginalHash, EXISTINGFILE.OriginalContent)
            .AddBinaryChunk(EXISTINGFILEWITHWRONGHASH.OriginalHash, EXISTINGFILEWITHWRONGHASH.OriginalContent)
            .AddTarChunk(out var TARHASH, t =>
            {
                t.AddBinary(TARCONTENT1.OriginalHash, TARCONTENT1.OriginalContent);
                t.AddBinary(TARCONTENT2.OriginalHash, TARCONTENT2.OriginalContent);
                t.AddBinary(TARCONTENT3.OriginalHash, TARCONTENT3.OriginalContent);
            })
            .Build();
        

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(NOTEXISTINGFILE.OriginalHash, 1, pfes =>
            {
                pfes.WithPointerFileEntry(NOTEXISTINGFILE.OriginalPath, NOTEXISTINGFILE.OriginalCreationDateTimeUtc,  NOTEXISTINGFILE.OriginalLastWriteTimeUtc);
            })
            .WithBinaryProperty(DUPLICATEBINARIES[0].OriginalHash, 1, pfes =>
            {
                pfes.WithPointerFileEntry(DUPLICATEBINARIES[0].OriginalPath)
                    .WithPointerFileEntry(DUPLICATEBINARIES[1].OriginalPath);
            })
            .WithBinaryProperty(EXISTINGFILE.OriginalHash,              EXISTINGFILE.OriginalContent.Length,              pfes => { pfes.WithPointerFileEntry(EXISTINGFILE.OriginalPath); })
            .WithBinaryProperty(EXISTINGFILEWITHWRONGHASH.OriginalHash, EXISTINGFILEWITHWRONGHASH.OriginalContent.Length, pfes => { pfes.WithPointerFileEntry(EXISTINGFILEWITHWRONGHASH.OriginalPath); })
            .WithBinaryProperty(TARCONTENT1.OriginalHash,                      TARHASH,                                         TARCONTENT1.OriginalContent.Length, pfes => { pfes.WithPointerFileEntry(TARCONTENT1.OriginalPath);})
            .WithBinaryProperty(TARCONTENT2.OriginalHash,                      TARHASH,                                         TARCONTENT2.OriginalContent.Length, pfes => { pfes.WithPointerFileEntry(TARCONTENT2.OriginalPath);})
            .WithBinaryProperty(TARCONTENT3.OriginalHash,                      TARHASH,                                         TARCONTENT3.OriginalContent.Length, pfes => { pfes.WithPointerFileEntry(TARCONTENT3.OriginalPath);})
            .BuildFake();


        var command = new RestoreCommandBuilder(fixture)
            .WithTargets($".{NOTEXISTINGFILE.OriginalPath}", "./Sam/")
            .WithIncludePointers(true)
            .Build();

        var hc = await new HandlerContextBuilder(command)
            .WithArchiveStorage(storageMock)
            .WithStateRepository(sr)
            .WithBaseFileSystem(fixture.FileSystem)
            .BuildAsync();

        // Act
        var result = await handler.Handle(hc, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();

            // The NOTEXISTINGFILE should be downloaded from storage and created on disk
        await storageMock.Received(1).OpenReadChunkAsync(NOTEXISTINGFILE.OriginalHash, Arg.Any<CancellationToken>());
        NOTEXISTINGFILE.FilePair.BinaryFile.ReadAllBytes().ShouldBe(NOTEXISTINGFILE.OriginalContent);
        (await hc.Hasher.GetHashAsync(NOTEXISTINGFILE.FilePair)).ShouldBe(NOTEXISTINGFILE.OriginalHash);
        // On Linux filesystems, creation and modification times may be synchronized due to filesystem limitations
        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            // On Linux, the timestamp that is set last will be used for both creation and modification time
            // Since we set CreationTime first, then LastWriteTime, both will end up being LastWriteTime
            NOTEXISTINGFILE.FilePair.CreationTimeUtc.ShouldBe(NOTEXISTINGFILE.OriginalLastWriteTimeUtc);
            NOTEXISTINGFILE.FilePair.LastWriteTimeUtc.ShouldBe(NOTEXISTINGFILE.OriginalLastWriteTimeUtc);
        }
        else
        {
            // On Windows, timestamps can be set independently
            NOTEXISTINGFILE.FilePair.CreationTimeUtc.ShouldBe(NOTEXISTINGFILE.OriginalCreationDateTimeUtc);
            NOTEXISTINGFILE.FilePair.LastWriteTimeUtc.ShouldBe(NOTEXISTINGFILE.OriginalLastWriteTimeUtc);
        }

            // The DUPLICATEBINARIES is downloaded twice and created on disk
        await storageMock.Received(2).OpenReadChunkAsync(DUPLICATEBINARIES[0].OriginalHash, Arg.Any<CancellationToken>());
        DUPLICATEBINARIES[0].FilePair.BinaryFile.ReadAllBytes().ShouldBe(DUPLICATEBINARIES[0].OriginalContent);
        DUPLICATEBINARIES[1].FilePair.BinaryFile.ReadAllBytes().ShouldBe(DUPLICATEBINARIES[0].OriginalContent);
        (await hc.Hasher.GetHashAsync(DUPLICATEBINARIES[0].FilePair)).ShouldBe(DUPLICATEBINARIES[0].OriginalHash);
        (await hc.Hasher.GetHashAsync(DUPLICATEBINARIES[1].FilePair)).ShouldBe(DUPLICATEBINARIES[0 /* not 1 to be double sure*/].OriginalHash);
        // Commenting since we re not setting this
        //DUPLICATEBINARIES[0].FilePair.BinaryFile.CreationTimeUtc.ShouldBe(DUPLICATEBINARIES[0].OriginalCreationDateTimeUtc);
        //DUPLICATEBINARIES[0].FilePair.BinaryFile.LastWriteTimeUtc.ShouldBe(DUPLICATEBINARIES[0].OriginalLastWriteTimeUtc);
        //DUPLICATEBINARIES[1].FilePair.BinaryFile.CreationTimeUtc.ShouldBe(DUPLICATEBINARIES[1].OriginalCreationDateTimeUtc);
        //DUPLICATEBINARIES[1].FilePair.BinaryFile.LastWriteTimeUtc.ShouldBe(DUPLICATEBINARIES[1].OriginalLastWriteTimeUtc);

        // The EXISTINGFILE is not downloaded and was not modified
        await storageMock.DidNotReceive().OpenReadChunkAsync(EXISTINGFILE.OriginalHash, Arg.Any<CancellationToken>());
        EXISTINGFILE.FilePair.BinaryFile.CreationTimeUtc.ShouldBe(EXISTINGFILE.OriginalCreationDateTimeUtc);
        EXISTINGFILE.FilePair.BinaryFile.LastWriteTimeUtc.ShouldBe(EXISTINGFILE.OriginalLastWriteTimeUtc);
        (await hc.Hasher.GetHashAsync(EXISTINGFILE.FilePair)).ShouldBe(EXISTINGFILE.OriginalHash);
        EXISTINGFILE.FilePair.PointerFile.ReadHash().ShouldBe(EXISTINGFILE.OriginalHash);

            // The EXISTINGFILEWITHWRONGHASH is downloaded again because the hash does not match
        await storageMock.Received(1).OpenReadChunkAsync(EXISTINGFILEWITHWRONGHASH.OriginalHash, Arg.Any<CancellationToken>());
        (await hc.Hasher.GetHashAsync(EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile)).ShouldBe(EXISTINGFILEWITHWRONGHASH.OriginalHash);
        // Commenting since we re not setting this
        //EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.CreationTimeUtc.ShouldBe(EXISTINGFILEWITHWRONGHASH.OriginalCreationDateTimeUtc);
        //EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.LastWriteTimeUtc.ShouldBe(EXISTINGFILEWITHWRONGHASH.OriginalLastWriteTimeUtc);
        EXISTINGFILEWITHWRONGHASH.FilePair.PointerFile.ReadHash().ShouldBe(EXISTINGFILEWITHWRONGHASH.OriginalHash);

            // The TARCONTENT files should be extracted from the same tar chunk
        await storageMock.Received(1).OpenReadChunkAsync(TARHASH, Arg.Any<CancellationToken>()); // !! the TAR binary is only downloaded once
        TARCONTENT1.FilePair.BinaryFile.ReadAllBytes().ShouldBe(TARCONTENT1.OriginalContent);
        (await hc.Hasher.GetHashAsync(TARCONTENT1.FilePair)).ShouldBe(TARCONTENT1.OriginalHash);
        TARCONTENT1.FilePair.CreationTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        TARCONTENT1.FilePair.LastWriteTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        TARCONTENT1.FilePair.PointerFile.ReadHash().ShouldBe(TARCONTENT1.OriginalHash);

        TARCONTENT2.FilePair.BinaryFile.ReadAllBytes().ShouldBe(TARCONTENT2.OriginalContent);
        (await hc.Hasher.GetHashAsync(TARCONTENT2.FilePair)).ShouldBe(TARCONTENT2.OriginalHash);
        TARCONTENT2.FilePair.CreationTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        TARCONTENT2.FilePair.LastWriteTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        TARCONTENT2.FilePair.PointerFile.ReadHash().ShouldBe(TARCONTENT2.OriginalHash);

        TARCONTENT3.FilePair.BinaryFile.ReadAllBytes().ShouldBe(TARCONTENT3.OriginalContent);
        (await hc.Hasher.GetHashAsync(TARCONTENT3.FilePair)).ShouldBe(TARCONTENT3.OriginalHash);
        TARCONTENT3.FilePair.CreationTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        TARCONTENT3.FilePair.LastWriteTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        TARCONTENT3.FilePair.PointerFile.ReadHash().ShouldBe(TARCONTENT3.OriginalHash);

        // Verify no other calls were made to storageMock
        storageMock.ReceivedCalls().Count().ShouldBe(6);
    }
    [Fact]
    public async Task GetChunkStreamAsync_InArchive_ButHydrated_UsesHydratedAndRestored() // OK
    {
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
            .WithBinaryProperty(BINARY.OriginalHash, BINARY.OriginalContent.Length, StorageTier.Archive, pfes => { pfes.WithPointerFileEntry(BINARY.OriginalPath); })
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
}