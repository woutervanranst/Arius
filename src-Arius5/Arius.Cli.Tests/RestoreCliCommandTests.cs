using Arius.Core.Commands;
using Wolverine;
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
    public async Task ExecuteAsync_WithAllOptions_SendsCorrectCommand()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var             busMock    = Substitute.For<IMessageBus>();
        busMock
            .InvokeAsync(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer --synchronize --download --keep-pointers";

        // Act
        var (exitCode, output, error) = await fixture.CallCliAsync(command, busMock);

        // Assert
        exitCode.ShouldBe(0);
        await busMock.Received(1).InvokeAsync(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

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
    public async Task ExecuteAsync_NoPath_NotInContainer_FailsWithMissingParameter()
    {
        // Arrange
        var command = $"restore --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        // Act
        var (exitCode, output, error) = await fixture.CallCliAsync(command);

        // Assert
        exitCode.ShouldBe(1);
        error.ShouldContain("Missing required parameter(s):\n<localroot>");
    }

    [Fact]
    public async Task ExecuteAsync_NoPath_InContainer_UsesArchiveRoot()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var             busMock    = Substitute.For<IMessageBus>();
        busMock
            .InvokeAsync(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
        var command = $"restore --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, busMock);

            // Assert
            exitCode.ShouldBe(0);
            await busMock.Received(1).InvokeAsync(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

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

    [Fact]
    public async Task ExecuteAsync_AccountKeyFromCli_NoEnvironmentVariable_UsesCliAccountKey()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var busMock = Substitute.For<IMessageBus>();
        busMock
            .InvokeAsync(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", null);
        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --accountkey testkeycli --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, busMock);

            // Assert
            exitCode.ShouldBe(0);
            capturedCommand.ShouldNotBeNull();
            capturedCommand.AccountKey.ShouldBe("testkeycli");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoAccountKey_NoEnvironmentVariable_FailsWithMissingParameter()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", null);
        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command);

            // Assert
            exitCode.ShouldBe(1);
            error.ShouldContain("Missing required option(s):\n-k|--accountkey");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoAccountKey_AccountKeyFromEnvironmentVariable_UsesEnvironmentVariableAccountKey()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var busMock = Substitute.For<IMessageBus>();
        busMock
            .InvokeAsync(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", "testkeyenv");
        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, busMock);

            // Assert
            exitCode.ShouldBe(0);
            capturedCommand.ShouldNotBeNull();
            capturedCommand.AccountKey.ShouldBe("testkeyenv");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AccountKeyFromCli_AccountKeyFromEnvironmentVariable_UsesCliAccountKey()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var busMock = Substitute.For<IMessageBus>();
        busMock
            .InvokeAsync(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", "testkeyenv");
        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --accountkey testkeycli --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, busMock);

            // Assert
            exitCode.ShouldBe(0);
            capturedCommand.ShouldNotBeNull();
            capturedCommand.AccountKey.ShouldBe("testkeycli");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", null);
        }
    }
}
