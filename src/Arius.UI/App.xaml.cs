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
using MessageBox = System.Windows.Forms.MessageBox;

namespace Arius.UI;

public partial class App
{
    private readonly IHost                    host;
    private          RepositoryFacade         repositoryFacade;

    public static string Name => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "PRODUCT_UNKNOWN"; // get the value of the <Product> in csproj

    public static string ApplicationDataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arius");

    public App()
    {
        host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        this.DispatcherUnhandledException += OnDispatcherUnhandledException;

        WeakReferenceMessenger.Default.Register<RepositoryChosenMessage>(this, OnRepositoryChosen);

        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) => MessageBox.Show(e.Exception.ToString(), "Unhandled exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)        => MessageBox.Show(e.ExceptionObject.ToString(), "Unhandled exception", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Facade>(new Facade(NullLoggerFactory.Instance));
        services.AddSingleton<ApplicationSettings>(new ApplicationSettings(Path.Combine(ApplicationDataPath, "arius.explorer.settings.sqlite")));
        
        // register the viewmodels - they are Transient - for every window a new one
        services.AddTransient<RepositoryChooserViewModel>(); 
        services.AddTransient<RepositoryExplorerViewModel>();

        // register the views - they are singletons
        services.AddSingleton<RepositoryChooserWindow>(sp => new RepositoryChooserWindow { DataContext   = sp.GetRequiredService<RepositoryChooserViewModel>() });
        services.AddSingleton<RepositoryExplorerWindow>(sp => new RepositoryExplorerWindow
        {
            DataContext = sp.GetRequiredService<RepositoryExplorerViewModel>()
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
            // Show a UI to choose a Repository
            var chooserWindow = host.Services.GetRequiredService<RepositoryChooserWindow>();
            chooserWindow.Owner = explorerWindow; // show modal over ExplorerWindow
            chooserWindow.ShowDialog();
        }
        else
        {
            // Load the last used Repository
            WeakReferenceMessenger.Default.Send(new RepositoryChosenMessage
            {
                Sender         = this,
                LocalDirectory = new DirectoryInfo(lastOpenedRepository.LocalDirectory),
                AccountName    = lastOpenedRepository.AccountName,
                AccountKey     = lastOpenedRepository.AccountKeyProtected.Unprotect(),
                ContainerName  = lastOpenedRepository.ContainerName,
                Passphrase     = lastOpenedRepository.PassphraseProtected.Unprotect()
            });
        }
    }

    private async void OnRepositoryChosen(object sender, RepositoryChosenMessage message)
    {
        if (message.Sender == this)
        {
            // Message sent during startup
        }
        else if (message.Sender is RepositoryChooserViewModel)
        {
            // Message sent by the RepositoryChooserWindow
            host.Services.GetRequiredService<RepositoryChooserWindow>().Close();
        }

        // Save
        var s = host.Services.GetRequiredService<ApplicationSettings>();
        s.AddLastUsedRepository(message);

        // Load RepositoryFacade
        repositoryFacade?.Dispose();
        repositoryFacade = await host.Services.GetRequiredService<Facade>()
            .ForStorageAccount(message.AccountName, message.AccountKey)
            .ForRepositoryAsync(message.ContainerName, message.Passphrase);

        // Update the ViewModel
        var explorerWindow = host.Services.GetRequiredService<RepositoryExplorerWindow>();
        var viewModel      = explorerWindow.DataContext as RepositoryExplorerViewModel;
        viewModel.LocalDirectory = message.LocalDirectory;
        viewModel.Repository     = repositoryFacade;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (host)
        {
            host.Dispose();
            repositoryFacade?.Dispose();
            await host.StopAsync(TimeSpan.FromSeconds(5)); // allow for graceful exit
        }

        base.OnExit(e);
    }
}