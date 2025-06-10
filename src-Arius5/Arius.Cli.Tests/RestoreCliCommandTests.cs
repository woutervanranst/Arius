using Arius.Core.Commands;
using MediatR;
using NSubstitute;
using Shouldly;

namespace Arius.Cli.Tests;

[Collection("CliCommandTests")] // make them run sequentially to avoid environment variable conflicts
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
        // Arrange
        RestoreCommand? capturedCommand = null;
        var             mediatorMock    = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer --synchronize --download --keep-pointers";

        // Act
        var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

        // Assert
        exitCode.ShouldBe(0);
        await mediatorMock.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

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

    [Fact]
    public async Task ExecuteAsync_NoPath_NotInContainer_ThrowsArgumentException()
    {
        // Arrange
        //Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
        var command = $"restore --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        // Act
        var (exitCode, output, error) = await fixture.CallCliAsync(command);

        // Assert
        exitCode.ShouldBe(1);
        error.ShouldContain("Missing required parameter(s):\n<localroot>");
    }

    [Fact]
    public async Task ExecuteAsync_NoPath_InContainer_UsesDefaultArchiveRoot()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var             mediatorMock    = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
        var command = $"restore --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

            // Assert
            exitCode.ShouldBe(0);
            await mediatorMock.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

            capturedCommand.ShouldNotBeNull();
            capturedCommand.LocalRoot.FullName.ShouldBe(new DirectoryInfo("/archive").FullName);
            capturedCommand.AccountName.ShouldBe("testaccount");
            capturedCommand.AccountKey.ShouldBe("testkey");
            capturedCommand.Passphrase.ShouldBe("testpass");
            capturedCommand.ContainerName.ShouldBe("testcontainer");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
        }
    }
}
