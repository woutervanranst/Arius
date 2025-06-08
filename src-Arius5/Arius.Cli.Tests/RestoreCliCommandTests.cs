using Arius.Core.Commands;
using MediatR;
using NSubstitute;

namespace Arius.Cli.Tests;

public sealed class RestoreCliCommandTests : IClassFixture<CliCommandTestsFixture>
{
    private readonly CliCommandTestsFixture fixture;

    public RestoreCliCommandTests(CliCommandTestsFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ExecuteAsync_WithAllOptions_SendsCorrectMediatRCommand()
    {
        // Arrange: Capture the command sent to IMediator
        RestoreCommand? capturedCommand = null;
        fixture.MediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        // Arrange: Set up the CLI arguments
        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer --synchronize --download --keep-pointers";

        // Act: Run the application
        var (exitCode, output) = await fixture.CallCliAsync(command);

        // Assert: Verify the outcome
        Assert.Equal(0, exitCode);
        await fixture.MediatorMock.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

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
