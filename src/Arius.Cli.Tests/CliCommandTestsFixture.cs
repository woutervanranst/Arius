using CliFx.Infrastructure;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text;

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

        var args = ParseArguments(command);
        var r    = await app.RunAsync(args);
        var o    = console.ReadOutputString();
        var e    = console.ReadErrorString();

        return (r, o, e);
    }

    public async Task<(int ExitCode, string Output, string Error)> CallCliAsync(string[] args, IMediator? mediatorMock = null)
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

        var r = await app.RunAsync(args);
        var o = console.ReadOutputString();
        var e = console.ReadErrorString();

        return (r, o, e);
    }

    private static string[] ParseArguments(string command)
    {
        var args = new List<string>();
        var currentArg = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < command.Length; i++)
        {
            char c = command[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (currentArg.Length > 0)
                {
                    args.Add(currentArg.ToString());
                    currentArg.Clear();
                }
            }
            else
            {
                currentArg.Append(c);
            }
        }

        if (currentArg.Length > 0)
        {
            args.Add(currentArg.ToString());
        }

        return args.ToArray();
    }
}
