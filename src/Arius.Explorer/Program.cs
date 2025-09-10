using System;
using Arius.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using System.Windows;

namespace Arius.Explorer;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // --- Serilog Configuration ---
        var logDirectory = "logs";
        var logPath = Path.Combine(logDirectory, $"arius-explorer-{DateTime.Now:yyyyMMdd_HHmmss}.log");

        // Configure the static logger instance
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.File(logPath,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Starting Arius Explorer...");

            var host = CreateHostBuilder(args).Build();
            
            // Start the host synchronously on STA thread
            host.Start();

            var app = new Application();
            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            app.Run(mainWindow);

            // Stop the host
            host.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Arius Explorer terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlushAsync().GetAwaiter().GetResult();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddUserSecrets(typeof(Program).Assembly);
            })
            .ConfigureServices((context, services) =>
            {
                // Register main window
                services.AddSingleton<MainWindow>();

                // Register Arius Core services
                services.AddArius(c =>
                {
                    c.MaxTokens = context.Configuration.GetValue<int>("Arius:MaxTokens", 5);
                });
            });
}