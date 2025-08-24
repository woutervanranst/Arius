using Arius.Core.Commands;
using Mediator;
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
    public async Task ExecuteAsync_WithAllOptions_SendsCorrectMediatorCommand()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var             mediatorMock    = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer --synchronize --download --keep-pointers";

        // Act
        var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

        // Assert
        exitCode.ShouldBe(0);
        await mediatorMock.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

        capturedCommand.ShouldNotBeNull();
        capturedCommand.Targets.ShouldBe([tempPath]);
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
        error.ShouldContain("Missing required parameter(s):\n<targets...>");
    }

    [Fact]
    public async Task ExecuteAsync_NoPath_InContainer_UsesArchiveRoot()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var             mediatorMock    = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
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
            capturedCommand.Targets.ShouldBe(["/archive"]);
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
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", null);
        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --accountkey testkeycli --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

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
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", "testkeyenv");
        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

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
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", "testkeyenv");
        var tempPath = Path.GetTempPath();
        var command = $"restore {tempPath} --accountname testaccount --accountkey testkeycli --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

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
    public async Task ExecuteAsync_SingleFile_SendsCorrectMediatorCommand()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        var tempFile = Path.GetTempFileName();
        var command = $"restore {tempFile} --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

            // Assert
            exitCode.ShouldBe(0);
            await mediatorMock.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

            capturedCommand.ShouldNotBeNull();
            capturedCommand.Targets.ShouldBe([tempFile]);
            capturedCommand.AccountName.ShouldBe("testaccount");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MultipleFiles_SendsCorrectMediatorCommand()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();
        var command = $"restore {tempFile1} {tempFile2} --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

            // Assert
            exitCode.ShouldBe(0);
            await mediatorMock.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

            capturedCommand.ShouldNotBeNull();
            capturedCommand.Targets.ShouldBe([tempFile1, tempFile2]);
            capturedCommand.AccountName.ShouldBe("testaccount");
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Directory_SendsCorrectMediatorCommand()
    {
        // Arrange
        RestoreCommand? capturedCommand = null;
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        var tempDir = Directory.CreateTempSubdirectory("restore-test");
        var command = $"restore {tempDir.FullName} --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

            // Assert
            exitCode.ShouldBe(0);
            await mediatorMock.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

            capturedCommand.ShouldNotBeNull();
            capturedCommand.Targets.ShouldBe([tempDir.FullName]);
            capturedCommand.AccountName.ShouldBe("testaccount");
        }
        finally
        {
            tempDir.Delete(true);
        }
    }
}
