using Arius.Core.Commands;
using Arius.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Logging.Testing;

namespace Arius.Core.Tests.Commands;

public class ArchiveCommandHandlerTests : IDisposable
{
    private readonly FakeLogger<ArchiveCommandHandler> logger;
    private readonly ArchiveCommandHandler             handler;
    private readonly Fixture                           fixture;

    public ArchiveCommandHandlerTests()
    {
        logger  = new();
        handler = new ArchiveCommandHandler(logger);
        fixture = new ();
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
    public async Task RunArchiveCommand()
    {
        var logger = new FakeLogger<ArchiveCommandHandler>();
        var ch     = new ArchiveCommandHandler(logger);
        var c      = CreateTestCommand() with
        {
            LocalRoot = new DirectoryInfo("C:\\Users\\WouterVanRanst\\Downloads\\Photos-001 (1)")
        };
        await ch.Handle(c, CancellationToken.None);

    }

    public void Dispose()
    {
        fixture?.Dispose();
    }
}