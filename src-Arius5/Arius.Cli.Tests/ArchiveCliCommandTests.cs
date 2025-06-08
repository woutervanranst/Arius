using Arius.Core.Commands;
using MediatR;
using NSubstitute;
using Xunit;

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
        Assert.Equal(0, exitCode);
        await fixture.MediatorMock.Received(1).Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>());

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
