using Arius.Core.Commands;
using MediatR;
using NSubstitute;
using Xunit;

namespace Arius.Cli.Tests;

public sealed class ArchiveCliCommandTests : IClassFixture<CliCommandTestsFixture>
{
    private readonly CliCommandTestsFixture _fixture;

    public ArchiveCliCommandTests(CliCommandTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExecuteAsync_WithAllOptions_SendsCorrectMediatRCommand()
    {
        // Arrange: Capture the command sent to IMediator
        ArchiveCommand? capturedCommand = null;
        _fixture.Mediator
            .Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<ArchiveCommand>());

        // Arrange: Set up the CLI arguments
        var tempPath = Path.GetTempPath();
        var args = new[]
        {
            "archive", tempPath,
            "--accountname", "testaccount",
            "--accountkey", "testkey",
            "--passphrase", "testpass",
            "--container", "testcontainer",
            //"--remove-local",
            //"--tier", "Hot"
        };

        // Act: Run the application
        var exitCode = await _fixture.RunAsync(args);

        // Assert: Verify the outcome
        Assert.Equal(0, exitCode);
        await _fixture.Mediator.Received(1).Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>());

        Assert.NotNull(capturedCommand);
        Assert.Equal(tempPath, capturedCommand.LocalRoot.FullName);
        Assert.Equal("testaccount", capturedCommand.AccountName);
        Assert.Equal("testkey", capturedCommand.AccountKey);
        Assert.Equal("testpass", capturedCommand.Passphrase);
        Assert.Equal("testcontainer", capturedCommand.ContainerName);
        //Assert.True(capturedCommand.RemoveLocal);
        //Assert.Equal(StorageTier.Hot, capturedCommand.Tier);
    }
}
