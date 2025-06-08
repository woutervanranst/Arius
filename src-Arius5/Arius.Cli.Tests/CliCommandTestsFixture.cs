using Arius.Core.Commands;
using CliFx;
using CliFx.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Arius.Cli.Tests;

public class CliCommandTestsFixture
{
    public IConsole Console { get; }
    public IMediator Mediator { get; }
    private IServiceCollection Services { get; }
    private CliApplicationBuilder ApplicationBuilder { get; }

    public CliCommandTestsFixture()
    {
        Console = new FakeInMemoryConsole();

        // Arrange: Substitute the IMediator
        Mediator = Substitute.For<IMediator>();

        // Arrange: Start with the REAL service collection from the application
        Services = Program.ConfigureServices(new ServiceCollection());

        // Arrange: REMOVE the real IMediator and REPLACE it with our substitute
        Services.RemoveAll<IMediator>();
        Services.AddSingleton(Mediator);

        // Arrange: Build the service provider for the test
        var serviceProvider = Services.BuildServiceProvider();

        // Arrange: Create the app builder, swapping in our modified services and a virtual console
        ApplicationBuilder = Program
            .CreateBuilder()
            .UseTypeActivator(serviceProvider.GetService)
            .UseConsole(Console);
    }

    public async Task<int> RunAsync(string[] args)
    {
        var app = ApplicationBuilder.Build();
        return await app.RunAsync(args);
    }
}
