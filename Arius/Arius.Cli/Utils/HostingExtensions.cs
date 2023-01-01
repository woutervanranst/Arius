using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console.Cli;
using System;

namespace Arius.Cli.Utils;

internal static class HostingExtensions
{
    public static IServiceCollection AddCommandLine(
        this IServiceCollection services,
        Action<IConfigurator> configurator)
    {
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(configurator);
        services.AddSingleton<ICommandApp>(app);

        return services;
    }

    //public static IServiceCollection AddCommandLine<TDefaultCommand>(
    //    this IServiceCollection services,
    //    Action<IConfigurator> configurator)
    //        where TDefaultCommand : class, ICommand
    //{
    //    var app = new CommandApp<TDefaultCommand>(new TypeRegistrar(services));
    //    app.Configure(configurator);
    //    services.AddSingleton<ICommandApp>(app);

    //    return services;
    //}

    public static int Run(this IHost host, string[] args)
    {
        if (host is null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        var app = host.Services.GetService<ICommandApp>();
        if (app == null)
        {
            throw new InvalidOperationException("Command application has not been configured.");
        }

        return app.Run(args);
    }
}
