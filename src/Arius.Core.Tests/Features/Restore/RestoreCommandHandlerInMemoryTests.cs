using Arius.Core.Features.Restore;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using NSubstitute;
using Shouldly;

namespace Arius.Core.Tests.Features.Restore;

public class RestoreCommandHandlerInMemoryTests : IClassFixture<InMemoryFileSystemFixture>
{
    private readonly InMemoryFileSystemFixture fixture;
    private readonly FakeLoggerFactory         loggerFactory = new();
    private readonly RestoreCommandHandler     handler;

    public RestoreCommandHandlerInMemoryTests(InMemoryFileSystemFixture fixture)
    {
        this.fixture  = fixture;
        handler       = new RestoreCommandHandler(loggerFactory.CreateLogger<RestoreCommandHandler>(), loggerFactory, fixture.AriusConfiguration);
    }

    [Fact]
    public async Task Restore_Mocked_HappyPath()
    {
        // Arrange
        var NOTEXISTINGFILE = fixture.FileSystem.WithSourceFolderHavingFilePair("/file1.jpg", 
            FilePairType.None, 10, 1, 
            creationTimeUtc: new DateTime(2017,  05, 25, 6, 0, 0, DateTimeKind.Utc), 
            lastWriteTimeUtc: new DateTime(2017, 05, 25, 7, 0, 0, DateTimeKind.Utc));


        var DUPLICATEBINARY = fixture.FileSystem.WithSourceFolderHavingFilePair("/Sam/file2.jpg", FilePairType.None, 10, 2);
        var DUPLICATEBINARY2 = DUPLICATEBINARY.WithDuplicate("/Sam/file2-duplicate.jpg");
        Assert.Equal(DUPLICATEBINARY.OriginalContent, DUPLICATEBINARY2.OriginalContent);

        var EXISTINGFILE = fixture.FileSystem.WithSourceFolderHavingFilePair("/Sam/file3.jpg", FilePairType.BinaryFileOnly, 1, 3, creationTimeUtc: StateRepositoryBuilder.DEFAULTUTCTIME, lastWriteTimeUtc: StateRepositoryBuilder.DEFAULTUTCTIME);
        
        var EXISTINGFILEWITHWRONGHASH = fixture.FileSystem.WithSourceFolderHavingFilePair("/Sam/file4.jpg", FilePairType.BinaryFileOnly, 1, 4);
        var existingFileWithWrongHashOriginalContent = EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.ReadAllBytes(); // TODO

        EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.WriteAllText("This file was overwritten");

        var command = new RestoreCommandBuilder(fixture)
            .WithTargets($".{NOTEXISTINGFILE.OriginalPath}", "./Sam/")
            .WithIncludePointers(true)
            .Build();

        var storageMock = new MockArchiveStorageBuilder(fixture)
            .AddBinaryChunk(NOTEXISTINGFILE.OriginalHash, NOTEXISTINGFILE.OriginalContent)
            .AddBinaryChunk(DUPLICATEBINARY.OriginalHash, DUPLICATEBINARY.OriginalContent)
            .AddBinaryChunk(EXISTINGFILE.OriginalHash, EXISTINGFILE.FilePair.BinaryFile.ReadAllBytes())
            .AddBinaryChunk(EXISTINGFILEWITHWRONGHASH.OriginalHash, existingFileWithWrongHashOriginalContent)
            .Build();



        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(NOTEXISTINGFILE.OriginalHash, 1, pfes =>
            {
                pfes.WithPointerFileEntry(NOTEXISTINGFILE.OriginalPath, NOTEXISTINGFILE.OriginalCreationDateTimeUtc,  NOTEXISTINGFILE.OriginalLastWriteTimeUtc);
            })
            .WithBinaryProperty(DUPLICATEBINARY.OriginalHash, 1, pfes =>
            {
                pfes.WithPointerFileEntry(DUPLICATEBINARY.OriginalPath)
                    .WithPointerFileEntry(DUPLICATEBINARY2.OriginalPath);
            })
            .WithBinaryProperty(EXISTINGFILE.OriginalHash, EXISTINGFILE.FilePair.Length!.Value, pfes => { pfes.WithPointerFileEntry(EXISTINGFILE.OriginalPath); })
            .WithBinaryProperty(EXISTINGFILEWITHWRONGHASH.OriginalHash, EXISTINGFILEWITHWRONGHASH.FilePair.Length!.Value, pfes => { pfes.WithPointerFileEntry(EXISTINGFILEWITHWRONGHASH.OriginalPath); })
            .BuildFake();


        var hc = await new HandlerContextBuilder(command)
            .WithArchiveStorage(storageMock)
            .WithStateRepository(sr)
            .WithBaseFileSystem(fixture.FileSystem)
            .BuildAsync();

        // Act
        var result = await handler.Handle(hc, CancellationToken.None);

        // Assert

            // The NOTEXISTINGFILE should be downloaded from storage and created on disk
        await storageMock.Received(1).OpenReadChunkAsync(NOTEXISTINGFILE.OriginalHash, Arg.Any<CancellationToken>());
        NOTEXISTINGFILE.FilePair.BinaryFile.ReadAllBytes().ShouldBe(NOTEXISTINGFILE.OriginalContent);
        (await hc.Hasher.GetHashAsync(NOTEXISTINGFILE.FilePair)).ShouldBe(NOTEXISTINGFILE.OriginalHash);
        NOTEXISTINGFILE.FilePair.CreationTimeUtc.ShouldBe(NOTEXISTINGFILE.OriginalCreationDateTimeUtc);
        NOTEXISTINGFILE.FilePair.LastWriteTimeUtc.ShouldBe(NOTEXISTINGFILE.OriginalLastWriteTimeUtc);

            // The DUPLICATEBINARY is downloaded twice and created on disk
        await storageMock.Received(2).OpenReadChunkAsync(DUPLICATEBINARY.OriginalHash, Arg.Any<CancellationToken>());
        DUPLICATEBINARY.FilePair.BinaryFile.ReadAllBytes().ShouldBe(DUPLICATEBINARY.OriginalContent);
        DUPLICATEBINARY2.FilePair.BinaryFile.ReadAllBytes().ShouldBe(DUPLICATEBINARY.OriginalContent);
        (await hc.Hasher.GetHashAsync(DUPLICATEBINARY.FilePair)).ShouldBe(DUPLICATEBINARY.OriginalHash);
        (await hc.Hasher.GetHashAsync(DUPLICATEBINARY2.FilePair)).ShouldBe(DUPLICATEBINARY.OriginalHash);
        DUPLICATEBINARY.FilePair.BinaryFile.CreationTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        DUPLICATEBINARY.FilePair.BinaryFile.LastWriteTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);

            // The EXISTINGFILE is not downloaded and was not modified
        await storageMock.DidNotReceive().OpenReadChunkAsync(EXISTINGFILE.OriginalHash, Arg.Any<CancellationToken>());
        EXISTINGFILE.FilePair.BinaryFile.CreationTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        EXISTINGFILE.FilePair.BinaryFile.LastWriteTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        (await hc.Hasher.GetHashAsync(EXISTINGFILE.FilePair)).ShouldBe(EXISTINGFILE.OriginalHash);
        EXISTINGFILE.FilePair.PointerFile.ReadHash().ShouldBe(EXISTINGFILE.OriginalHash);

            // The EXISTINGFILEWITHWRONGHASH is downloaded again because the hash does not match
        await storageMock.Received(1).OpenReadChunkAsync(EXISTINGFILEWITHWRONGHASH.OriginalHash, Arg.Any<CancellationToken>());
        (await hc.Hasher.GetHashAsync(EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile)).ShouldBe(EXISTINGFILEWITHWRONGHASH.OriginalHash);
        EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.CreationTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.LastWriteTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        EXISTINGFILEWITHWRONGHASH.FilePair.PointerFile.ReadHash().ShouldBe(EXISTINGFILEWITHWRONGHASH.OriginalHash);


        // Verify no other calls were made to storageMock
        storageMock.ReceivedCalls().Count().ShouldBe(5);
    }
}