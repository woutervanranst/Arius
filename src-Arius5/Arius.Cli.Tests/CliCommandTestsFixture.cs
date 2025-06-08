using CliFx;
using CliFx.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Arius.Cli.Tests;

public class CliCommandTestsFixture
{
    private FakeInMemoryConsole   Console            { get; }
    public  IMediator             MediatorMock       { get; }
    private CliApplicationBuilder ApplicationBuilder { get; }

    public CliCommandTestsFixture()
    {
        Console      = new FakeInMemoryConsole();
        MediatorMock = Substitute.For<IMediator>();

        var serviceProvider = Program.ConfigureServices(new ServiceCollection())
            .RemoveAll<IMediator>()
            .AddSingleton(MediatorMock)
            .BuildServiceProvider();

        // Arrange: Create the app builder, swapping in our modified services and a virtual console
        ApplicationBuilder = Program
            .CreateBuilder()
            .UseTypeActivator(serviceProvider.GetService)
            .UseConsole(Console);
    }

    public async Task<(int ExitCode, string Output)> CallCliAsync(string command)
    {
        var args = command.Split(' ');
        var app  = ApplicationBuilder.Build();
        var r    = await app.RunAsync(args);
        var s    = Console.ReadOutputString();

        return (r, s);
    }
}
