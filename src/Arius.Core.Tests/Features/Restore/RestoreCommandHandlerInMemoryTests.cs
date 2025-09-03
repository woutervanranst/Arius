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
using System.Security.Cryptography;
using System.Text;
using Zio;

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

    private static Hash GenerateValidHash(string seed)
    {
        var seedBytes = Encoding.UTF8.GetBytes(seed);
        var hashBytes = SHA256.HashData(seedBytes);
        return Hash.FromBytes(hashBytes);
    }

    [Fact]
    public async Task Restore_Mocked_HappyPath()
    {
        // Arrange
        var NOTEXISTINGFILE_PATH             = "/file1.jpg";
        var NOTEXISTINGFILE_CREATIONDATETIMEUTC = new DateTime(2017, 05, 25, 6, 0, 0, DateTimeKind.Utc);
        var NOTEXISTINGFILE_LASTWRITETIMEUTC = new DateTime(2017, 05, 25, 7, 0, 0, DateTimeKind.Utc);
        var (NOTEXISTINGFILE_HASH, NOTEXISTINGFILE_CONTENT) = FakeDataGenerator.GenerateRandomContent(10, 1);


        var DUPLICATEBINARY1_PATH = "/Sam/file2.jpg";
        var DUPLICATEBINARY2_PATH = "/Sam/file2-duplicate.jpg";
        var (DUPLICATEBINARY_HASH, DUPLICATEBINARY_CONTENT) = FakeDataGenerator.GenerateRandomContent(10, 2);

        var EXISTINGFILE = fixture.FileSystem.WithSourceFolderHavingFilePair("/Sam/file3.jpg", FilePairType.BinaryFileOnly, 1, 3, creationTimeUtc: StateRepositoryBuilder.DEFAULTUTCTIME, lastWriteTimeUtc: StateRepositoryBuilder.DEFAULTUTCTIME);
        

        var EXISTINGFILEWITHWRONGHASH = fixture.FileSystem.WithSourceFolderHavingFilePair("/Sam/file4.jpg", FilePairType.BinaryFileOnly, 1, 4);
        

        var fakeContent = new Dictionary<Hash, byte[]>
        {
            {NOTEXISTINGFILE_HASH, NOTEXISTINGFILE_CONTENT},
            {DUPLICATEBINARY_HASH, DUPLICATEBINARY_CONTENT},
            {EXISTINGFILE.Hash, EXISTINGFILE.FilePair.BinaryFile.ReadAllBytes()},
            {EXISTINGFILEWITHWRONGHASH.Hash, EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.ReadAllBytes()}
        };


        EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.WriteAllText("This file was overwritten");


        var command = new RestoreCommandBuilder(fixture)
            .WithTargets($".{NOTEXISTINGFILE_PATH}", "./Sam/")
            .WithIncludePointers(true)
            .Build();


        var storageMock = Substitute.For<IArchiveStorage>();
        storageMock.ContainerExistsAsync()
            .Returns(Task.FromResult(true));
        storageMock.OpenReadChunkAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var hash    = callInfo.Arg<Hash>();
                return Task.FromResult<Stream>(new MemoryStream(fakeContent[hash]));
            });



        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(NOTEXISTINGFILE_HASH, 1, pfes =>
            {
                pfes.WithPointerFileEntry(NOTEXISTINGFILE_PATH, NOTEXISTINGFILE_CREATIONDATETIMEUTC, NOTEXISTINGFILE_LASTWRITETIMEUTC);
            })
            .WithBinaryProperty(DUPLICATEBINARY_HASH, 1, pfes =>
            {
                pfes.WithPointerFileEntry(DUPLICATEBINARY1_PATH)
                    .WithPointerFileEntry(DUPLICATEBINARY2_PATH);
            })
            .WithBinaryProperty(EXISTINGFILE.Hash, EXISTINGFILE.FilePair.Length!.Value, pfes => { pfes.WithPointerFileEntry(EXISTINGFILE.FilePair.FullName); })
            .WithBinaryProperty(EXISTINGFILEWITHWRONGHASH.Hash, EXISTINGFILEWITHWRONGHASH.FilePair.Length!.Value, pfes => { pfes.WithPointerFileEntry(EXISTINGFILEWITHWRONGHASH.FilePair.FullName); })
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
        await storageMock.Received(1).OpenReadChunkAsync(NOTEXISTINGFILE_HASH, Arg.Any<CancellationToken>());
        var nef = FilePair.FromBinaryFilePath(hc.FileSystem, NOTEXISTINGFILE_PATH);
        nef.BinaryFile.ReadAllBytes().ShouldBe(NOTEXISTINGFILE_CONTENT);
        (await hc.Hasher.GetHashAsync(nef)).ShouldBe(NOTEXISTINGFILE_HASH);
        nef.CreationTimeUtc.ShouldBe(NOTEXISTINGFILE_CREATIONDATETIMEUTC);
        nef.LastWriteTimeUtc.ShouldBe(NOTEXISTINGFILE_LASTWRITETIMEUTC);

            // The DUPLICATEBINARY is downloaded twice and created on disk
        await storageMock.Received(2).OpenReadChunkAsync(DUPLICATEBINARY_HASH, Arg.Any<CancellationToken>());
        var db1 = FilePair.FromBinaryFilePath(hc.FileSystem, DUPLICATEBINARY1_PATH);
        var db2 = FilePair.FromBinaryFilePath(hc.FileSystem, DUPLICATEBINARY2_PATH);
        db1.BinaryFile.ReadAllBytes().ShouldBe(DUPLICATEBINARY_CONTENT);
        db2.BinaryFile.ReadAllBytes().ShouldBe(DUPLICATEBINARY_CONTENT);
        (await hc.Hasher.GetHashAsync(db1)).ShouldBe(DUPLICATEBINARY_HASH);
        (await hc.Hasher.GetHashAsync(db2)).ShouldBe(DUPLICATEBINARY_HASH);
        db1.BinaryFile.CreationTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        db2.BinaryFile.LastWriteTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);

            // The EXISTINGFILE is not downloaded and was not modified
        await storageMock.DidNotReceive().OpenReadChunkAsync(EXISTINGFILE.Hash, Arg.Any<CancellationToken>());
        EXISTINGFILE.FilePair.BinaryFile.CreationTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        EXISTINGFILE.FilePair.BinaryFile.LastWriteTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        (await hc.Hasher.GetHashAsync(EXISTINGFILE.FilePair)).ShouldBe(EXISTINGFILE.Hash);
        EXISTINGFILE.FilePair.PointerFile.ReadHash().ShouldBe(EXISTINGFILE.Hash);

            // The EXISTINGFILEWITHWRONGHASH is downloaded again because the hash does not match
        await storageMock.Received(1).OpenReadChunkAsync(EXISTINGFILEWITHWRONGHASH.Hash, Arg.Any<CancellationToken>());
        (await hc.Hasher.GetHashAsync(EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile)).ShouldBe(EXISTINGFILEWITHWRONGHASH.Hash);
        EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.CreationTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        EXISTINGFILEWITHWRONGHASH.FilePair.BinaryFile.LastWriteTimeUtc.ShouldBe(StateRepositoryBuilder.DEFAULTUTCTIME);
        EXISTINGFILEWITHWRONGHASH.FilePair.PointerFile.ReadHash().ShouldBe(EXISTINGFILEWITHWRONGHASH.Hash);


        // Verify no other calls were made to storageMock
        storageMock.ReceivedCalls().Count().ShouldBe(5);
    }
}