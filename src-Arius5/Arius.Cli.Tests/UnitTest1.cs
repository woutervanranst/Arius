// In your Test Project (e.g., Arius.Cli.Tests)

using Arius.Cli; // To access Program.cs
using Arius.Core.Commands;
using Arius.Core.Models;
using CliFx.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Arius.Cli.Tests;

public sealed class ArchiveCliCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithAllOptions_SendsCorrectMediatRCommand()
    {
        // Arrange: Substitute the core logic dependency (MediatR)
        var mediator = Substitute.For<IMediator>();
        ArchiveCommand? capturedCommand = null;

        // When Send is called, capture the command argument and return a completed task
        mediator
            .Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Unit.Value))
            .AndDoes(callInfo =>
            {
                capturedCommand = callInfo.Arg<ArchiveCommand>();
            });

        // Arrange: Build a bespoke service provider for the test
        var services = new ServiceCollection();
        services.AddSingleton(mediator); // Use the substitute
        services.AddTransient<ArchiveCliCommand>(); // The command under test
        var serviceProvider = services.BuildServiceProvider();

        // Arrange: Set up the CLI arguments
        var tempPath = Path.GetTempPath();
        var args = new[]
        {
            "archive",
            tempPath,
            "--accountname", "testaccount",
            "--accountkey", "testkey",
            "--passphrase", "testpass",
            "--container", "testcontainer",
            "--remove-local",
            "--tier", "Hot",
            "--fasthash"
        };

        // Arrange: Create the app, swapping in our substitute services and a virtual console
        var app = Program
            .CreateBuilder()
            .UseTypeActivator(serviceProvider.GetService)
            //.UseConsole(new VirtualConsole()) // Suppress console output
            .Build();

        // Act: Run the application with the specified arguments
        var exitCode = await app.RunAsync(args);

        // Assert: Verify the outcome
        Assert.Equal(0, exitCode); // Command should succeed

        // Verify that MediatR's Send method was called exactly once with the correct command type
        await mediator.Received(1).Send(Arg.Any<ArchiveCommand>(), Arg.Any<CancellationToken>());

        // Verify that the captured command has the correct values from the CLI
        Assert.NotNull(capturedCommand);
        Assert.Equal(tempPath, capturedCommand.LocalRoot.FullName.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Equal("testaccount", capturedCommand.AccountName);
        Assert.Equal("testkey", capturedCommand.AccountKey);
        Assert.Equal("testpass", capturedCommand.Passphrase);
        Assert.Equal("testcontainer", capturedCommand.ContainerName);
        Assert.True(capturedCommand.RemoveLocal);
        Assert.Equal(StorageTier.Hot, capturedCommand.Tier);
        Assert.NotNull(capturedCommand.ProgressReporter);
    }
}