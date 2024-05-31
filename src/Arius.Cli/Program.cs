using System.Runtime.CompilerServices;
using Arius.Cli.Commands;
using Arius.Core.Application;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

[assembly: InternalsVisibleTo("Arius.Cli.Tests")]

namespace Arius.Cli;

internal class CommandApp
{
    static int Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        var app = CreateCommandApp(services);
        return app.Run(args);
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddArius();
        services.AddScoped<ArchiveCommandCli>();
    }

    public static Spectre.Console.Cli.CommandApp CreateCommandApp(IServiceCollection services)
    {
        var registrar = new TypeRegistrar(services);
        var app       = new Spectre.Console.Cli.CommandApp(registrar);

        app.Configure(config => { config.AddCommand<ArchiveCommandCli>("archive"); });

        return app;
    }
}


