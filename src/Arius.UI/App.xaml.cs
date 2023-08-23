using System.IO;
using Arius.Core.Facade;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;

namespace Arius.UI;

public partial class App
{
    private readonly IHost                    host;
    private          RepositoryChooserWindow  chooserWindow;
    private          RepositoryExplorerWindow explorerWindow;
    private          RepositoryFacade         repositoryFacade;

    public static string Name => System.Reflection.Assembly.GetEntryAssembly().GetName().Name;

    public App()
    {
        host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        var messenger = host.Services.GetRequiredService<IMessenger>();
        messenger.Register<RepositoryChosenMessage>(this, OnRepositoryChosen);

        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configure your services here

        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        
        services.AddSingleton<Facade>(new Facade(NullLoggerFactory.Instance));
        
        services.AddTransient<ChooseRepositoryViewModel>();
        services.AddTransient<ExploreRepositoryViewModel>();

        services.AddTransient<RepositoryChooserWindow>();
        services.AddTransient<RepositoryExplorerWindow>();

    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await host.StartAsync();
        //chooserWindow = new RepositoryChooserWindow
        //{
        //    DataContext = host.Services.GetRequiredService<ChooseRepositoryViewModel>()
        //};
        //chooserWindow.Show();

        // DEBUG PURPOSES
        var f   = host.Services.GetRequiredService<Facade>();
        var saf = f.ForStorageAccount(Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME"), Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY"));
        repositoryFacade = await saf.ForRepositoryAsync("test", "woutervr");

        var viewModel = host.Services.GetRequiredService<ExploreRepositoryViewModel>();
        viewModel.Repository     = repositoryFacade;
        viewModel.LocalDirectory = new DirectoryInfo("C:\\Users\\woute\\Documents\\AriusTest");

        //var x = await repositoryFacade.GetEntriesAsync().ToArrayAsync();

        explorerWindow = new RepositoryExplorerWindow
        {
            DataContext = viewModel
        };
        explorerWindow.Show();
    }

    private void OnRepositoryChosen(object sender, RepositoryChosenMessage message)
    {
        chooserWindow.Hide();

        repositoryFacade?.Dispose();
        repositoryFacade = message.ChosenRepository;

        var viewModel = host.Services.GetRequiredService<ExploreRepositoryViewModel>();
        viewModel.Repository     = message.ChosenRepository;
        viewModel.LocalDirectory = message.LocalDirectory;

        explorerWindow = new RepositoryExplorerWindow
        {
            DataContext = viewModel
        };
        explorerWindow.Show();
        chooserWindow.Close();
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