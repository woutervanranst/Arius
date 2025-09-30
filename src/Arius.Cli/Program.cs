using Arius.Cli.CliCommands;
using Arius.Core;
using CliFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.IO;
using System.Reflection;

namespace Arius.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // --- Serilog Configuration ---
        var isRunningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        var logDirectory         = isRunningInContainer ? "/logs" : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arius", "logs");
        var logPath              = Path.Join(logDirectory, $"arius-{DateTime.Now:yyyyMMdd_HHmmss}.log");

        Directory.CreateDirectory(logDirectory);

        // Configure the static logger instance
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug() // Capture all levels of logs
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.With<ShortSourceContextEnricher>()
            .WriteTo.File(logPath,
                //rollingInterval: RollingInterval.Day, // Not strictly needed for unique files, but good practice
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{ShortSourceContext}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var exitCode = 0;

        try
        {
            Log.Information("Starting Arius CLI...");
            Log.Information("Version: {Version}", GetVersion());

            exitCode = await CreateBuilder()
                .UseTypeActivator(CreateServiceProvider().GetService)
                .Build()
                .RunAsync(args);

            if (exitCode == 0)
            {
                Log.Information("Arius CLI finished successfully.");
            }
            else
            {
                Log.Warning("Arius CLI completed with a non-zero exit code: {ExitCode}", exitCode);
            }
        }
        catch (Exception ex)
        {
            exitCode = exitCode == 0 ? 1 : exitCode;
            Log.Fatal(ex, "A fatal error occurred while executing Arius CLI.");
        }
        finally
        {
            try
            {
                // Ensure all buffered logs are written to the file before exiting
                await Log.CloseAndFlushAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to flush logs: {ex.Message}");
                exitCode = exitCode == 0 ? 1 : exitCode;
            }
        }

        return exitCode;
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

    private static string GetVersion()
    {
        //// Prefer informational version so that suffixes like "local" are preserved
        //var version = typeof(Program).Assembly
        //    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(version))
            return version;

        return typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private class ShortSourceContextEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContextValue) &&
                sourceContextValue is ScalarValue scalarValue &&
                scalarValue.Value is string fullName)
            {
                var shortName = fullName.Split('.').LastOrDefault() ?? fullName;
                var property  = propertyFactory.CreateProperty("ShortSourceContext", shortName);
                logEvent.AddPropertyIfAbsent(property);
            }
        }
    }
}