using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace Arius.Explorer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            if (ServiceProvider == null)
                throw new InvalidOperationException("ServiceProvider not initialized");

            var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Application starting up");
            
            // Get the repository explorer window from DI
            var repositoryWindow = ServiceProvider.GetRequiredService<RepositoryExplorer.Window>();
            MainWindow = repositoryWindow;
            repositoryWindow.Show();
            
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            var logger = ServiceProvider?.GetService<ILogger<App>>();
            logger?.LogError(ex, "Error during application startup");
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var logger = ServiceProvider?.GetService<ILogger<App>>();
        logger?.LogInformation("Application shutting down");
        base.OnExit(e);
    }

    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var logger = ServiceProvider?.GetService<ILogger<App>>();
        logger?.LogError(e.Exception, "Unhandled WPF exception");
        
        // You can choose to handle the exception or let it crash
        // e.Handled = true; // Uncomment to prevent crash
    }
}