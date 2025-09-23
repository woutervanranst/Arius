using Arius.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;

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

            // Set the service provider for the App
            App.ServiceProvider = host.Services;

            // Create and run the WPF application on STA thread
            var app = new App();
            app.Run();

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
                // Register application settings
                services.AddSingleton<Settings.IApplicationSettings>(provider => Settings.ApplicationSettings.Default);
                services.AddSingleton<Settings.IRecentRepositoryManager, Settings.RecentRepositoryManager>();


                // Register windows and viewmodels
                services.AddTransient<RepositoryExplorer.Window>();
                services.AddTransient<RepositoryExplorer.RepositoryExplorerViewModel>();
                services.AddTransient<ChooseRepository.Window>();
                services.AddTransient<ChooseRepository.ChooseRepositoryViewModel>();

                // Register Arius Core services
                services.AddArius(c =>
                {
                    c.MaxTokens = context.Configuration.GetValue<int>("Arius:MaxTokens", 5);
                });
            });
}