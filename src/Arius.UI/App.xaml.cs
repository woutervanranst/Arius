using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Arius.Core.Facade;
using Arius.Core.New;
using Arius.UI.Messages;
using Arius.UI.Services;
using Arius.UI.Utils;
using Arius.UI.ViewModels;
using Arius.UI.Views;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace Arius.UI;

public partial class App
{
    private readonly IHost host;

    public static string Name => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "PRODUCT_UNKNOWN"; // get the value of the <Product> in csproj

    public static string ApplicationDataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arius");

    public App()
    {
        host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        this.DispatcherUnhandledException          += OnDispatcherUnhandledException;

        WeakReferenceMessenger.Default.Register<CloseChooseRepositoryWindowMessage>(this, CloseChooseRepositoryWindowMessageHandler);

        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private void CloseChooseRepositoryWindowMessageHandler(object recipient, CloseChooseRepositoryWindowMessage message)
    {
        Application.Current.Windows.OfType<ChooseRepositoryWindow>().Single().Close();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) => MessageBox.Show(e.Exception.ToString(), "Unhandled exception", MessageBoxButton.OK, MessageBoxImage.Error);
    private void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)        => MessageBox.Show(e.ExceptionObject.ToString(), "Unhandled exception", MessageBoxButton.OK, MessageBoxImage.Error);

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddArius();
        services.AddSingleton<Facade>(new Facade(NullLoggerFactory.Instance));
        services.AddSingleton<ApplicationSettings>(new ApplicationSettings(Path.Combine(ApplicationDataPath, "arius.explorer.settings.sqlite")));

        // register the viewmodels - they are Transient - for every window a new one
        services.AddTransient<ChooseRepositoryViewModel>();
        services.AddTransient<ExploreRepositoryViewModel>();

        // register the views
            // the RepositoryExplorerWindow is a singleton, we only need to show it once
        services.AddSingleton<ExploreRepositoryWindow>(sp => new ExploreRepositoryWindow { DataContext = sp.GetRequiredService<ExploreRepositoryViewModel>() });
            // the RepositoryChooserWindow is transient, we can show it multiple times (a closed window cannot be reopened)
        services.AddTransient<ChooseRepositoryWindow>(sp => new ChooseRepositoryWindow { DataContext = sp.GetRequiredService<ChooseRepositoryViewModel>() });
        //services.AddSingleton<IRepositoryChooserWindowFactory, RepositoryChooserWindowFactory>();

        services.AddSingleton<IDialogService, DialogService>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await host.StartAsync();

        var explorerWindow = host.Services.GetRequiredService<ExploreRepositoryWindow>();
        explorerWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (host)
        {
            host.Dispose();
            await host.StopAsync(TimeSpan.FromSeconds(5)); // allow for graceful exit
        }

        base.OnExit(e);
    }
}