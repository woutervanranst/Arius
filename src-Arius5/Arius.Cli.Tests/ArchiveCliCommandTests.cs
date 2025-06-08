using Arius.Core.Commands;
using Arius.Core.Models;
using MediatR;
using NSubstitute;
using Shouldly;

namespace Arius.Cli.Tests;

public sealed class ArchiveCliCommandTests : IClassFixture<CliCommandTestsFixture>
{
    private readonly CliCommandTestsFixture fixture;

    public ArchiveCliCommandTests(CliCommandTestsFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ExecuteAsync_WithAllOptions_SendsCorrectMediatRCommand()
    {
        // Arrange: Capture the command sent to IMediator
        ArchiveCommand? capturedCommand = null;
        fixture.MediatorMock
            .Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<ArchiveCommand>());

        // Arrange: Set up the CLI arguments
        var tempPath = Path.GetTempPath();
        var command = $"archive {tempPath} --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        // Act: Run the application
        var (exitCode, output) = await fixture.CallCliAsync(command);

        // Assert: Verify the outcome
        exitCode.ShouldBe(0);
        await fixture.MediatorMock.Received(1).Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>());

        capturedCommand.ShouldNotBeNull();
        capturedCommand.LocalRoot.FullName.ShouldBe(tempPath);
        capturedCommand.AccountName.ShouldBe("testaccount");
        capturedCommand.AccountKey.ShouldBe("testkey");
        capturedCommand.Passphrase.ShouldBe("testpass");
        capturedCommand.ContainerName.ShouldBe("testcontainer");
        capturedCommand.RemoveLocal.ShouldBeFalse();
        capturedCommand.Tier.ShouldBe(StorageTier.Archive);
    }
}
