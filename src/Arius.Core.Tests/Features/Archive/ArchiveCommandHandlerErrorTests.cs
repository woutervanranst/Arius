using Arius.Core.Features.Archive;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;

namespace Arius.Core.Tests.Features.Archive;

public class ArchiveCommandHandlerErrorTests : IDisposable
{
    private readonly FakeLogger<ArchiveCommandHandler> logger;
    private readonly ArchiveCommandHandler             handler;
    private readonly Fixture        fixture;

    public ArchiveCommandHandlerErrorTests()
    {
        logger  = new();
        fixture = new Fixture();
        handler = new ArchiveCommandHandler(logger, NullLoggerFactory.Instance, fixture.AriusConfiguration);
    }


    [Fact]
    public async Task Handle_WithMalformedStorageAccoutKey_ShouldFail()
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
        e.Message.ShouldContain("Invalid account credentials format");
    }

    [Fact]
    public async Task Handle_WithInvalidAzureCredentials_ShouldFail()
    {
        // Arrange
        var command = new ArchiveCommandBuilder(fixture)
            .WithAccountName("nonexistentaccount")
            .WithAccountKey(GenerateFakeStorageAccountKey())
            .WithUseRetryPolicy(false)
            .Build();

        // Act
        var act = () => handler.Handle(command, CancellationToken.None).AsTask();

        // Assert
        var e = await Should.ThrowAsync<InvalidOperationException>(act);
        e.Message.ShouldContain("Failed to create or access Azure Storage container");

        static string GenerateFakeStorageAccountKey()
        {
            byte[] keyBytes = new byte[64];
            Random.Shared.NextBytes(keyBytes);
            return Convert.ToBase64String(keyBytes);
        }
    }

    public void Dispose()
    {
        fixture?.Dispose();
    }
}