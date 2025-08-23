using Arius.Core.Commands;
using Arius.Core.Models;
using Mediator;
using NSubstitute;
using Shouldly;

namespace Arius.Cli.Tests;

[Collection("CliCommandTests")] // make them run sequentially to avoid environment variable conflicts
public sealed class ArchiveCliCommandTests : IClassFixture<CliCommandTestsFixture>
{
    private readonly CliCommandTestsFixture fixture;

    public ArchiveCliCommandTests(CliCommandTestsFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ExecuteAsync_WithAllOptions_SendsCorrectMediatorCommand()
    {
        // Arrange: Capture the command sent to IMediator
        ArchiveCommand? capturedCommand = null;
        var             mediatorMock    = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<ArchiveCommand>());

        // Arrange: Set up the CLI arguments
        var tempPath = Path.GetTempPath();
        var command = $"archive {tempPath} --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        // Act: Run the application
        var (exitCode, output, error) = await fixture.CallCliAsync(command, mediatorMock);

        // Assert: Verify the outcome
        exitCode.ShouldBe(0);
        await mediatorMock.Received(1).Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>());

        capturedCommand.ShouldNotBeNull();
        capturedCommand.LocalRoot.FullName.ShouldBe(tempPath);
        capturedCommand.AccountName.ShouldBe("testaccount");
        capturedCommand.AccountKey.ShouldBe("testkey");
        capturedCommand.Passphrase.ShouldBe("testpass");
        capturedCommand.ContainerName.ShouldBe("testcontainer");
        capturedCommand.RemoveLocal.ShouldBeFalse();
        capturedCommand.Tier.ShouldBe(StorageTier.Archive);
    }

    [Fact]
    public async Task ExecuteAsync_NoPath_NotInContainer_FailsWithMissingParameter()
    {
        // Arrange
        var command = $"archive --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

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
        ArchiveCommand? capturedCommand = null;
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<ArchiveCommand>());

        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
        var command = $"archive --accountname testaccount --accountkey testkey --passphrase testpass --container testcontainer";

        try
        {
            // Act
            var (exitCode, output, error)= await fixture.CallCliAsync(command, mediatorMock);

            // Assert
            exitCode.ShouldBe(0);
            await mediatorMock.Received(1).Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>());

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
        ArchiveCommand? capturedCommand = null;
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<ArchiveCommand>());

        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", null);
        var tempPath = Path.GetTempPath();
        var command = $"archive {tempPath} --accountname testaccount --accountkey testkeycli --passphrase testpass --container testcontainer";

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
        var command = $"archive {tempPath} --accountname testaccount --passphrase testpass --container testcontainer";

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
        ArchiveCommand? capturedCommand = null;
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<ArchiveCommand>());

        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", "testkeyenv");
        var tempPath = Path.GetTempPath();
        var command = $"archive {tempPath} --accountname testaccount --passphrase testpass --container testcontainer";

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
        ArchiveCommand? capturedCommand = null;
        var mediatorMock = Substitute.For<IMediator>();
        mediatorMock
            .Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Unit>(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<ArchiveCommand>());

        Environment.SetEnvironmentVariable("ARIUS_ACCOUNT_KEY", "testkeyenv");
        var tempPath = Path.GetTempPath();
        var command = $"archive {tempPath} --accountname testaccount --accountkey testkeycli --passphrase testpass --container testcontainer";

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
}
