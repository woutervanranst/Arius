using Arius.Core.Facade;
using Arius.UI.Extensions;
using Arius.UI.Utils;
using Arius.UI.ViewModels;
using Arius.UI.Views;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace Arius.UI;

public partial class App
{
    private readonly IHost             host;

    public static string Name => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "PRODUCT_UNKNOWN"; // get the value of the <Product> in csproj

    public static string ApplicationDataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arius");

    public App()
    {
        host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        this.DispatcherUnhandledException          += OnDispatcherUnhandledException;

        WeakReferenceMessenger.Default.Register<ChooseRepositoryMessage>(this, OnChooseRepository);
        WeakReferenceMessenger.Default.Register<RepositoryChosenMessage>(this, OnRepositoryChosen);

        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) => MessageBox.Show(e.Exception.ToString(), "Unhandled exception", MessageBoxButton.OK, MessageBoxImage.Error);
    private void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)        => MessageBox.Show(e.ExceptionObject.ToString(), "Unhandled exception", MessageBoxButton.OK, MessageBoxImage.Error);

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Facade>(new Facade(NullLoggerFactory.Instance));
        services.AddSingleton<ApplicationSettings>(new ApplicationSettings(Path.Combine(ApplicationDataPath, "arius.explorer.settings.sqlite")));

        // register the viewmodels - they are Transient - for every window a new one
        services.AddTransient<ChooseRepositoryViewModel>();
        services.AddTransient<ExploreRepositoryViewModel>();

        // register the views
        // the RepositoryExplorerWindow is a singleton, we only need to show it once
        services.AddSingleton<RepositoryExplorerWindow>(sp => new RepositoryExplorerWindow
        {
            DataContext = sp.GetRequiredService<ExploreRepositoryViewModel>()
        });
        // the RepositoryChooserWindow is transient, we can show it multiple times (a closed window cannot be reopened)
        services.AddTransient<RepositoryChooserWindow>(sp => new RepositoryChooserWindow
        {
            DataContext = sp.GetRequiredService<ChooseRepositoryViewModel>()
        });
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await host.StartAsync();

        var explorerWindow = host.Services.GetRequiredService<RepositoryExplorerWindow>();
        explorerWindow.Show();

        var s = host.Services.GetRequiredService<ApplicationSettings>();
        var lastOpenedRepository = s.RecentRepositories.FirstOrDefault();
        if (lastOpenedRepository is null)
        {
            // Show the ChooseRepositoryWindow with empty fields
            WeakReferenceMessenger.Default.Send(new ChooseRepositoryMessage());
        }
        else
        {
            // Show the ExporeRepositoryWindow with the last used Repository
            WeakReferenceMessenger.Default.Send(new RepositoryChosenMessage(this, lastOpenedRepository));
        }
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

    // -- CHOOSE REPOSITORY --

    private RepositoryChooserWindow? chooserWindow;

    private void OnChooseRepository(object recipient, ChooseRepositoryMessage message)
    {
        // Show a UI to choose a Repository
        chooserWindow = host.Services.GetRequiredService<RepositoryChooserWindow>();
        WeakReferenceMessenger.Default.Send(message); // do this after the GetRequiredServices - then the ViewModel is instantiated

        var explorerWindow = host.Services.GetRequiredService<RepositoryExplorerWindow>();
        chooserWindow.Owner = explorerWindow; // show modal over ExplorerWindow
        chooserWindow.ShowDialog();
    }

    private async void OnRepositoryChosen(object recipient, RepositoryChosenMessage message)
    {
        if (message.Sender == this)
        {
            // Message sent during startup
        }
        else if (message.Sender is ChooseRepositoryViewModel)
        {
            // Message sent by the RepositoryChooserWindow
            chooserWindow!.Close();
        }

        var explorerWindow = host.Services.GetRequiredService<RepositoryExplorerWindow>();
        WeakReferenceMessenger.Default.Send(message); // do this after the GetRequiredServices - then the ViewModel is instantiated
    }
}