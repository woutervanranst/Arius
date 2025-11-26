using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Windows;

namespace Arius.Explorer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static string Name => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "PRODUCT_UNKNOWN"; // get the value of the <Product> in csproj

    public static IServiceProvider? ServiceProvider { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Set up global exception handlers
        SetupGlobalExceptionHandlers();

        try
        {
            if (ServiceProvider == null)
                throw new InvalidOperationException("ServiceProvider not initialized");

            var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Application starting up");

            // Upgrade settings from previous ClickOnce version if needed
            try
            {
                if (Settings.ApplicationSettings.Default.UpgradeRequired)
                {
                    logger.LogInformation("Upgrading application settings from previous version");
                    Settings.ApplicationSettings.Default.Upgrade();
                    Settings.ApplicationSettings.Default.UpgradeRequired = false;
                    Settings.ApplicationSettings.Default.Save();
                    logger.LogInformation("Settings upgraded successfully");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Settings upgrade failed, continuing with default settings");
            }

            // Get the repository explorer window from DI
            var repositoryWindow = ServiceProvider.GetRequiredService<RepositoryExplorer.RepositoryExplorerWindow>();
            MainWindow = repositoryWindow;
            repositoryWindow.Show();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            var logger = ServiceProvider?.GetService<ILogger<App>>();
            logger?.LogError(ex, "Error during application startup");
            ShowExceptionMessageBox("Startup Error", ex);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var logger = ServiceProvider?.GetService<ILogger<App>>();
        logger?.LogInformation("Application shutting down");
        base.OnExit(e);
    }

    private void SetupGlobalExceptionHandlers()
    {
        // WPF UI thread exceptions
        DispatcherUnhandledException += Application_DispatcherUnhandledException;

        // Non-UI thread exceptions
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Task exceptions
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        static void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            var logger = ServiceProvider?.GetService<ILogger<App>>();
            logger?.LogError(e.Exception, "Unhandled WPF exception");

            ShowExceptionMessageBox("Unhandled Exception", e.Exception);

            // Mark as handled to prevent crash
            e.Handled = true;
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var logger    = ServiceProvider?.GetService<ILogger<App>>();
            var exception = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception");
            logger?.LogError(exception, "Unhandled domain exception");

            ShowExceptionMessageBox("Critical Error", exception);
        }

        static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            var logger = ServiceProvider?.GetService<ILogger<App>>();
            logger?.LogError(e.Exception, "Unhandled task exception");

            ShowExceptionMessageBox("Task Exception", e.Exception);

            // Mark as observed to prevent crash
            e.SetObserved();
        }
    }

    private static void ShowExceptionMessageBox(string title, Exception exception)
    {
        var message = $"An unexpected error occurred:\n\n{exception.Message}";

        if (exception.InnerException != null)
        {
            message += $"\n\nInner Exception:\n{exception.InnerException.Message}";
        }

        message += $"\n\nException Type: {exception.GetType().Name}";

        MessageBox.Show(message, $"{Name} - {title}", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}