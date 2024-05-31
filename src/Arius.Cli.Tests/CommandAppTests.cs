using Arius.Core.Application.Commands;
using NSubstitute;

namespace Arius.Cli.Tests;

public class CommandAppTests : IClassFixture<CommandAppFixture>
{
    private readonly CommandAppFixture _fixture;

    public CommandAppTests(CommandAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Main_Should_Send_ArchiveCommand_To_Mediator()
    {
        // Arrange
        var args = new[] { "archive", "test.txt" };

        // Act
        await _fixture.CommandApp.RunAsync(args);

        // Assert
        await _fixture.Mediator.Received(1).Send(
            Arg.Is<ArchiveCommand>(cmd => cmd.FilePath == "test.txt"),
            Arg.Any<CancellationToken>());
    }
}