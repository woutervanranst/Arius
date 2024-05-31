using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Arius.Cli.Tests;

public class CommandAppFixture : IDisposable
{
    public IMediator                      Mediator   { get; private set; }
    public Spectre.Console.Cli.CommandApp CommandApp { get; private set; }

    public CommandAppFixture()
    {
        var services = new ServiceCollection();
        Mediator = Substitute.For<IMediator>();
        services.AddSingleton(Mediator);

        Program.ConfigureServices(services);
        CommandApp = Program.CreateCommandApp(services);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}