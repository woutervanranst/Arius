using Arius.Core.Features.Archive;
using Arius.Core.Tests.Builders;
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


    [Fact]
    public async Task Handle_WithInvalidAzureCredentials_ShouldFail()
    {
        // Arrange
        var command = new ArchiveCommandBuilder(fixture)
            .WithAccountName("nonexistentaccount")
            .WithAccountKey("invalid_key_that_will_cause_authentication_failure")
            .Build();

        // Act
        var act = () => handler.Handle(command, CancellationToken.None).AsTask();

        // Assert
        var e = await Should.ThrowAsync<FormatException>(act);
        e.Message.ShouldContain("No valid combination of account information found.");
    }

    public void Dispose()
    {
        fixture?.Dispose();
    }
}