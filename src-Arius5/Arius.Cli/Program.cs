using Arius.Core;
using CliFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows.Input;

namespace Arius.Cli;

internal static class Program
{
    // Main entry point for production
    public static async Task<int> Main(string[] args) =>
        await CreateBuilder()
            .UseTypeActivator(CreateServiceProvider().GetService)
            .Build()
            .RunAsync(args);

    // Exposed for testability, allowing tests to customize the builder
    public static CliApplicationBuilder CreateBuilder() =>
        new CliApplicationBuilder()
            .AddCommandsFromThisAssembly()
            .SetExecutableName("arius");

    // Production DI registrations
    public static IServiceProvider CreateServiceProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddArius(c =>
        {
            c.MaxTokens = builder.Configuration.GetValue<int>("Arius:MaxTokens", 5); // Default to 5 if not set
        });

        // Add other services
        builder.Services.AddApplicationInsightsTelemetryWorkerService();

        //// Register all CliFx commands in this assembly for DI
        //// This allows them to have dependencies injected
        //foreach (var commandType in typeof(Program).Assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(ICommand)) && !t.IsAbstract))
        //{
        //    builder.Services.AddTransient(commandType);
        //}

        return builder.Services.BuildServiceProvider();
    }
}