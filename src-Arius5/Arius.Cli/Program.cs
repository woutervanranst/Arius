using Arius.Core;
using CliFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arius.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await CreateBuilder()
            .UseTypeActivator(CreateServiceProvider().GetService)
            .Build()
            .RunAsync(args);
    }

    public static CliApplicationBuilder CreateBuilder() =>
        new CliApplicationBuilder()
            .AddCommandsFromThisAssembly() // Command discovery
        ;

    public static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    public static IServiceCollection ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // Register Arius Core services
        services.AddArius(c =>
        {
            c.MaxTokens = configuration.GetValue<int>("Arius:MaxTokens", 5);
        });

        services.AddApplicationInsightsTelemetryWorkerService();

        // Register commands in the DI to allow creation of the commands through the Activator
        foreach (var commandType in typeof(Program).Assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(ICommand)) && !t.IsAbstract))
        {
            services.AddTransient(commandType);
        }

        return services;
    }
}