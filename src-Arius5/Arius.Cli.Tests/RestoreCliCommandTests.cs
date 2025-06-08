using Arius.Core.Commands;
using MediatR;
using NSubstitute;
using Shouldly;

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
        exitCode.ShouldBe(0);
        await fixture.MediatorMock.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

        capturedCommand.ShouldNotBeNull();
        capturedCommand.LocalRoot.FullName.ShouldBe(tempPath);
        capturedCommand.AccountName.ShouldBe("testaccount");
        capturedCommand.AccountKey.ShouldBe("testkey");
        capturedCommand.Passphrase.ShouldBe("testpass");
        capturedCommand.ContainerName.ShouldBe("testcontainer");
        capturedCommand.Synchronize.ShouldBeTrue();
        capturedCommand.Download.ShouldBeTrue();
        capturedCommand.KeepPointers.ShouldBeTrue();
    }
}
