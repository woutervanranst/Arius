using Arius.Core.Features.Commands.Restore;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.FakeLogger;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using NSubstitute;
using Shouldly;

namespace Arius.Core.Tests.Features.Commands.Restore;

public class RestoreCommandHandlerTests : IClassFixture<FixtureWithFileSystem>
{
    private readonly FixtureWithFileSystem fixture;
    private readonly FakeLoggerFactory     fakeLoggerFactory = new();
    private readonly RestoreCommandHandler handler;

    public RestoreCommandHandlerTests(FixtureWithFileSystem fixture)
    {
        this.fixture  = fixture;
        handler       = new RestoreCommandHandler(fakeLoggerFactory.CreateLogger<RestoreCommandHandler>(), fakeLoggerFactory, fixture.AriusConfiguration);
    }

    [Fact]
    public async Task Restore_OnePointerFile_CreateOrOverwritePointerFileOnDiskTEMP() // NOTE temp skipped by CI
    {
        // Arrange
        var command = new RestoreCommandBuilder(fixture)
            .WithLocalRoot(fixture.TestRunSourceFolder)
            .WithContainerName("test")
            //.WithTargets("./IMG20250126195020.jpg", "./Sam/")
            .WithTargets("./invoice.pdf")
            .WithIncludePointers(true)
            .Build();

        // TODO directory without trailing /

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        // Should create or overwrite the pointer file on disk
        //true.ShouldBe(false, "Test not implemented");
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
            .AddChunks_BinaryChunk(NOTEXISTINGFILE.OriginalHash, NOTEXISTINGFILE.OriginalContent)
            .AddChunks_BinaryChunk(DUPLICATEBINARIES[0].OriginalHash, DUPLICATEBINARIES[1].OriginalContent)
            .AddChunks_BinaryChunk(EXISTINGFILE.OriginalHash, EXISTINGFILE.OriginalContent)
            .AddChunks_BinaryChunk(EXISTINGFILEWITHWRONGHASH.OriginalHash, EXISTINGFILEWITHWRONGHASH.OriginalContent)
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
            .WithFakeFile(EXISTINGFILE)
            .WithFakeFile(EXISTINGFILEWITHWRONGHASH)
            .WithBinaryProperty(TARCONTENT1.OriginalHash,                      TARHASH,                                         TARCONTENT1.OriginalContent.Length, pfes => { pfes.WithPointerFileEntry(TARCONTENT1.OriginalPath);})
            .WithBinaryProperty(TARCONTENT2.OriginalHash,                      TARHASH,                                         TARCONTENT2.OriginalContent.Length, pfes => { pfes.WithPointerFileEntry(TARCONTENT2.OriginalPath);})
            .WithBinaryProperty(TARCONTENT3.OriginalHash,                      TARHASH,                                         TARCONTENT3.OriginalContent.Length, pfes => { pfes.WithPointerFileEntry(TARCONTENT3.OriginalPath);})
            .BuildFake();


        var command = new RestoreCommandBuilder(fixture)
            .WithTargets($".{NOTEXISTINGFILE.OriginalPath}", "./Sam/")
            .WithIncludePointers(true)
            .Build();

        var hc = await new HandlerContextBuilder(command, fakeLoggerFactory)
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
}