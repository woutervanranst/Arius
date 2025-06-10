using CliFx.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arius.Cli.Tests;

public class CliCommandTestsFixture
{
    public CliCommandTestsFixture()
    {
    }

    public async Task<(int ExitCode, string Output, string Error)> CallCliAsync(string command, IMediator? mediatorMock = null)
    {
        using var console = new FakeInMemoryConsole();

        var serviceProvider = mediatorMock is null
            ? Program.ConfigureServices(new ServiceCollection())
                .BuildServiceProvider()
            : Program.ConfigureServices(new ServiceCollection())
                .RemoveAll<IMediator>()
                .AddSingleton(mediatorMock)
                .BuildServiceProvider();

        var app = Program
            .CreateBuilder()
            .UseTypeActivator(serviceProvider.GetService)
            .UseConsole(console)
            .Build();

        var args = command.Split(' ');
        var r    = await app.RunAsync(args);
        var o    = console.ReadOutputString();
        var e    = console.ReadErrorString();

        return (r, o, e);
    }
}
