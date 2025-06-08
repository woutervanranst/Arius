using Arius.Core.Commands;
using MediatR;
using NSubstitute;
using Xunit;

namespace Arius.Cli.Tests;

public sealed class RestoreCliCommandTests : IClassFixture<CliCommandTestsFixture>
{
    private readonly CliCommandTestsFixture _fixture;

    public RestoreCliCommandTests(CliCommandTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExecuteAsync_WithAllOptions_SendsCorrectMediatRCommand()
    {
        // Arrange: Capture the command sent to IMediator
        RestoreCommand? capturedCommand = null;
        _fixture.Mediator
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        // Arrange: Set up the CLI arguments
        var tempPath = Path.GetTempPath();
        var args = new[]
        {
            "restore", tempPath,
            "--accountname", "testaccount",
            "--accountkey", "testkey",
            "--passphrase", "testpass",
            "--container", "testcontainer",
            "--synchronize",
            "--download",
            "--keep-pointers"
        };

        // Act: Run the application
        var exitCode = await _fixture.RunAsync(args);

        // Assert: Verify the outcome
        Assert.Equal(0, exitCode);
        await _fixture.Mediator.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

        Assert.NotNull(capturedCommand);
        Assert.Equal(tempPath, capturedCommand.LocalRoot.FullName);
        Assert.Equal("testaccount", capturedCommand.AccountName);
        Assert.Equal("testkey", capturedCommand.AccountKey);
        Assert.Equal("testpass", capturedCommand.Passphrase);
        Assert.Equal("testcontainer", capturedCommand.ContainerName);
        Assert.True(capturedCommand.Synchronize);
        Assert.True(capturedCommand.Download);
        Assert.True(capturedCommand.KeepPointers);
    }
}
