using Arius.Core;
using CliFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arius.Cli;

internal static class Program
{
    // Main entry point for production
    public static async Task<int> Main(string[] args) =>
        await CreateBuilder()
            .UseTypeActivator(CreateServiceProvider().GetService)
            .Build()
            .RunAsync(args);

    public static CliApplicationBuilder CreateBuilder() =>
        new CliApplicationBuilder()
            .AddCommandsFromThisAssembly()
            .SetExecutableName("arius");

    public static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    public static IServiceCollection ConfigureServices(IServiceCollection services)
    {
        // Build configuration to be used for service registration
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        // Register the configuration instance itself
        services.AddSingleton<IConfiguration>(configuration);

        // Register Arius Core services, reading MaxTokens from configuration
        services.AddArius(c =>
        {
            c.MaxTokens = configuration.GetValue<int>("Arius:MaxTokens", 5);
        });

        // Add other production services
        services.AddApplicationInsightsTelemetryWorkerService();

        // Register all CliFx commands in this assembly for DI
        foreach (var commandType in typeof(Program).Assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(ICommand)) && !t.IsAbstract))
        {
            services.AddTransient(commandType);
        }

        return services;
    }
}