using Arius.Core.Features.Restore;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Extensions;
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
        var NOTEXISTINGFILE_PATH = "/file1.jpg";
        var NOTEXISTINGFILE_HASH = GenerateValidHash("file1-hash");

        var DUPLICATEBINARY1_PATH = "/Sam/file2.jpg";
        var DUPLICATEBINARY2_PATH = "/Sam/file2-duplicate.jpg";
        var DUPLICATEBINARY_HASH  = GenerateValidHash("file2-hash");
        
        var EXISTINGFILE = fixture.FileSystem.WithSourceFolderHavingFilePair("/Sam/file3.jpg", FilePairType.BinaryFileOnly, 1, 1);


        var command = new RestoreCommandBuilder(fixture)
            .WithTargets($".{NOTEXISTINGFILE_PATH}", "./Sam/")
            .Build();


        var storageMock = Substitute.For<IArchiveStorage>();
        storageMock.ContainerExistsAsync()
            .Returns(Task.FromResult(true));
        storageMock.OpenReadChunkAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => 
            {
                var hash = callInfo.Arg<Hash>();
                var content = $"This is test file content for the stream {hash}";
                return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)));
            });

        

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(NOTEXISTINGFILE_HASH, 1, pfes =>
            {
                pfes.WithPointerFileEntry(NOTEXISTINGFILE_PATH);
            })
            .WithBinaryProperty(DUPLICATEBINARY_HASH, 1, pfes =>
            {
                pfes.WithPointerFileEntry(DUPLICATEBINARY1_PATH)
                    .WithPointerFileEntry(DUPLICATEBINARY2_PATH);
            })
            .WithBinaryProperty(EXISTINGFILE.Hash, EXISTINGFILE.FilePair.Length!.Value, pfes =>
            {
                pfes.WithPointerFileEntry(EXISTINGFILE.FilePair.FullName);
            })
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
        fixture.FileSystem.ReadAllText(NOTEXISTINGFILE_PATH).ShouldStartWith("This is test file content for the stream");
        fixture.FileSystem.ReadAllText(NOTEXISTINGFILE_PATH).ShouldContain(NOTEXISTINGFILE_HASH.ToString());

            // The DUPLICATEBINARY is downloaded twice and created on disk
        await storageMock.Received(2).OpenReadChunkAsync(DUPLICATEBINARY_HASH, Arg.Any<CancellationToken>());
        fixture.FileSystem.ReadAllText(DUPLICATEBINARY1_PATH).ShouldContain(DUPLICATEBINARY_HASH.ToString());
        fixture.FileSystem.ReadAllText(DUPLICATEBINARY2_PATH).ShouldContain(DUPLICATEBINARY_HASH.ToString());

            // The EXISTINGFILE is not downloaded
        await storageMock.DidNotReceive().OpenReadChunkAsync(EXISTINGFILE.Hash, Arg.Any<CancellationToken>());

            // Verify no other calls were made to storageMock
        storageMock.ReceivedCalls().Count().ShouldBe(4);
    }
}