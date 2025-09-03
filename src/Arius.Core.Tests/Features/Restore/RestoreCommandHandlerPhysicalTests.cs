using Arius.Core.Features.Restore;
using Arius.Core.Shared.Hashing;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using System.Security.Cryptography;
using System.Text;

namespace Arius.Core.Tests.Features.Restore;

public class RestoreCommandHandlerPhysicalTests : IClassFixture<PhysicalFileSystemFixture>
{
    private readonly PhysicalFileSystemFixture        fixture;
    private readonly FakeLogger<RestoreCommandHandler> logger;
    private readonly RestoreCommandHandler             handler;

    public RestoreCommandHandlerPhysicalTests(PhysicalFileSystemFixture fixture)
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
    public async Task Restore_OnePointerFile_CreateOrOverwritePointerFileOnDisk()
    {
        // Arrange
        var command = new RestoreCommandBuilder(fixture)
            .WithLocalRoot(fixture.TestRunSourceFolder)
            .WithContainerName("test")
            .WithTargets("./IMG20250126195020.jpg", "./Sam/")
            .Build();

        // TODO directory without trailing /

        // Act
        var result = await handler.Handle(command, CancellationToken.None);
        
        // Assert
        // Should create or overwrite the pointer file on disk
        //true.ShouldBe(false, "Test not implemented");
    }
}