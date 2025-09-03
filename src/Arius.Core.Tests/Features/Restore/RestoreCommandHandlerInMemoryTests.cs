using Arius.Core.Features.Restore;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using System.Security.Cryptography;
using System.Text;
using Zio.FileSystems;

namespace Arius.Core.Tests.Features.Restore;

public class RestoreCommandHandlerInMemoryTests : IClassFixture<InMemoryFileSystemFixture>
{
    private readonly InMemoryFileSystemFixture        fixture;
    private readonly FakeLogger<RestoreCommandHandler> logger;
    private readonly RestoreCommandHandler             handler;

    public RestoreCommandHandlerInMemoryTests(InMemoryFileSystemFixture fixture)
    {
        this.fixture = fixture;
        logger       = new();
        handler      = new RestoreCommandHandler(logger, NullLoggerFactory.Instance, fixture.AriusConfiguration);
    }

    private static Hash GenerateValidHash(string seed)
    {
        var seedBytes = Encoding.UTF8.GetBytes(seed);
        var hashBytes = SHA256.HashData(seedBytes);
        return Hash.FromBytes(hashBytes);
    }

    [Fact]
    public async Task RestoreWithMockedStorage_ShouldProcessPointerFiles()
    {
        // Arrange
        var command = new RestoreCommandBuilder(fixture)
            .WithContainerName("test")
            .WithPassphrase("woutervr")
            .WithTargets("./file1.jpg", "./Sam/")
            .Build();

        var storage = Substitute.For<IArchiveStorage>();
        storage.ContainerExistsAsync()
            .Returns(Task.FromResult(true));
        storage.OpenReadChunkAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<Stream>(new MemoryStream("This is test file content for the stream"u8.ToArray())));

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(GenerateValidHash("file1-hash"), 1)
            .WithPointerFileEntry("/file1.jpg")
            .WithBinaryProperty(GenerateValidHash("file2-hash"), 1)
            .WithPointerFileEntry("/Sam/file2.jpg")
            .WithPointerFileEntry("/Sam/file2-duplicate.jpg")
            .BuildFake();

        using var mfs = new MemoryFileSystem();

        var hc = await new HandlerContextBuilder(command)
            .WithArchiveStorage(storage)
            .WithStateRepository(sr)
            .WithBaseFileSystem(mfs)
            .BuildAsync();

        // Act
        var result = await handler.Handle(hc, CancellationToken.None);

        // Assert
        // Add appropriate assertions here when the test logic is implemented
    }

    // Additional unit tests with mocked dependencies can be added here
}