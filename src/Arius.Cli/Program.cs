using Arius.Cli.CliCommands;
using Arius.Core;
using CliFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Arius.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // --- Serilog Configuration ---
        var isRunningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        var logDirectory         = isRunningInContainer ? "/logs" : "logs";
        var logPath              = Path.Combine(logDirectory, $"arius-{DateTime.Now:yyyyMMdd_HHmmss}.log");

        // Configure the static logger instance
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug() // Capture all levels of logs
            .Enrich.FromLogContext()
            .WriteTo.File(logPath,
                //rollingInterval: RollingInterval.Day, // Not strictly needed for unique files, but good practice
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Starting Arius CLI...");

            return await CreateBuilder()
                .UseTypeActivator(CreateServiceProvider().GetService)
                .Build()
                .RunAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1; // Return a non-zero exit code for failure
        }
        finally
        {
            Log.Information("Arius CLI finished.");
            // Ensure all buffered logs are written to the file before exiting
            await Log.CloseAndFlushAsync();
        }
    }

    public static CliApplicationBuilder CreateBuilder()
    {
        var isRunningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

        // Command discovery
        //return new CliApplicationBuilder().AddCommandsFromThisAssembly();

        var builder = new CliApplicationBuilder()
            .SetTitle("arius")
            .SetExecutableName("arius")
            .SetVersion($"v{GetVersion()}");

        if (isRunningInContainer)
        {
            builder.AddCommands([
                typeof(ArchiveDockerCliCommand),
                typeof(RestoreDockerCliCommand)]);
        }
        else
        {
            builder.AddCommands([
                typeof(ArchiveCliCommand),
                typeof(RestoreCliCommand)]);
        }

        return builder;

        static string GetVersion()
        {
            // Prefer informational version so that suffixes like "local" are preserved
            var informational = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational;

            return typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }

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

        // --- Serilog Integration with DI ---
        services.AddLogging(loggingBuilder =>
            loggingBuilder.AddSerilog(dispose: true));

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