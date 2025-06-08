using Arius.Core.Commands;
using CliFx.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Arius.Cli.Tests;

public sealed class RestoreCliCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WithAllOptions_SendsCorrectMediatRCommand()
    {
        using var console = new FakeInMemoryConsole();

        // Arrange: Substitute the IMediator
        var mediator = Substitute.For<IMediator>();
        RestoreCommand? capturedCommand = null;

        mediator
            .Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Unit.Value))
            .AndDoes(callInfo => capturedCommand = callInfo.Arg<RestoreCommand>());

        // Arrange: Start with the REAL service collection from the application
        var services = Program.ConfigureServices(new ServiceCollection());

        // Arrange: REMOVE the real IMediator and REPLACE it with our substitute
        services.RemoveAll<IMediator>();
        services.AddSingleton(mediator);

        // Arrange: Build the service provider for the test
        var serviceProvider = services.BuildServiceProvider();

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

        // Arrange: Create the app, swapping in our modified services and a virtual console
        var app = Program
            .CreateBuilder()
            .UseTypeActivator(serviceProvider.GetService)
            .UseConsole(console)
            .Build();

        // Act: Run the application
        var exitCode = await app.RunAsync(args);

        // Assert: Verify the outcome
        Assert.Equal(0, exitCode);
        await mediator.Received(1).Send(Arg.Any<RestoreCommand>(), Arg.Any<CancellationToken>());

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
