using Arius.Core.Features.Restore;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fixtures;
using NSubstitute;
using System.Security.Cryptography;
using System.Text;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Tests.Helpers.Extensions;
using Zio.FileSystems;

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
        var command = new RestoreCommandBuilder(fixture)
            .WithContainerName("containername")
            .WithTargets("./file1.jpg", "./Sam/")
            .Build();

        var storage = Substitute.For<IArchiveStorage>();
        storage.ContainerExistsAsync()
            .Returns(Task.FromResult(true));
        storage.OpenReadChunkAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<Stream>(new MemoryStream("This is test file content for the stream"u8.ToArray())));

        var samFile3 = fixture.FileSystem.WithSourceFolderHavingFilePair("/Sam/file3.jpg", FilePairType.BinaryFileOnly, 1, 1);

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(GenerateValidHash("file1-hash"), 1, pfes =>
            {
                pfes.WithPointerFileEntry("/file1.jpg");
            })
            .WithBinaryProperty(GenerateValidHash("file2-hash"), 1, pfes =>
            {
                pfes.WithPointerFileEntry("/Sam/file2.jpg")
                    .WithPointerFileEntry("/Sam/file2-duplicate.jpg");
            })
            .WithBinaryProperty(samFile3.Hash, samFile3.FilePair.Length!.Value, pfes =>
            {
                pfes.WithPointerFileEntry("/Sam/file3.jpg");
            })
            .BuildFake();

        var hc = await new HandlerContextBuilder(command)
            .WithArchiveStorage(storage)
            .WithStateRepository(sr)
            .WithBaseFileSystem(fixture.FileSystem)
            .BuildAsync();

        // Act
        var result = await handler.Handle(hc, CancellationToken.None);

        // Assert

        // file1-hash is downloaded once
        // file2-hash is downloaded twice
        // fp1 binary is NOT downloaded

        // Add appropriate assertions here when the test logic is implemented
    }

    // Additional unit tests with mocked dependencies can be added here
}