using Arius.Core.Commands;
using Arius.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;

namespace Arius.Core.Tests.Commands;

public class ArchiveCommandHandlerErrorTests : IDisposable
{
    private readonly FakeLogger<ArchiveCommandHandler> logger;
    private readonly ArchiveCommandHandler             handler;
    private readonly Fixture                           fixture;

    public ArchiveCommandHandlerErrorTests()
    {
        logger  = new();
        fixture = new ();
        handler = new ArchiveCommandHandler(logger, NullLoggerFactory.Instance, fixture.AriusConfiguration);
    }

    private ArchiveCommand CreateTestCommand()
    {
        return new ArchiveCommand
        {
            AccountName       = fixture.RepositoryOptions.AccountName,
            AccountKey        = fixture.RepositoryOptions.AccountKey,
            ContainerName     = $"{fixture.RepositoryOptions.ContainerName}-{DateTime.UtcNow.Ticks}-{Random.Shared.Next()}",
            Passphrase        = fixture.RepositoryOptions.Passphrase,
            RemoveLocal       = false,
            Tier              = StorageTier.Cool,
            LocalRoot         = fixture.TestRunSourceFolder,
            Parallelism       = 1,
            SmallFileBoundary = 2 * 1024 * 1024
        };
    }

    [Fact]
    public async Task Handle_WithInvalidAzureCredentials_ShouldFail()
    {
        // Arrange
        var command = CreateTestCommand();
        command = command with 
        {
            AccountName = "nonexistentaccount",
            AccountKey = "invalid_key_that_will_cause_authentication_failure"
        };

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        var e = await Should.ThrowAsync<FormatException>(act);
        e.Message.ShouldContain("No valid combination of account information found.");
    }

    public void Dispose()
    {
        fixture?.Dispose();
    }
}