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
        chooserWindow = new RepositoryChooserWindow
        {
            DataContext = host.Services.GetRequiredService<ChooseRepositoryViewModel>()
        };
        chooserWindow.Show();
    }

    private void OnRepositoryChosen(object sender, RepositoryChosenMessage message)
    {
        chooserWindow.Hide();

        repositoryFacade?.Dispose();
        repositoryFacade = message.ChosenRepository;

        var viewModel = host.Services.GetRequiredService<ExploreRepositoryViewModel>();
        viewModel.Repository = message.ChosenRepository;

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
            repositoryFacade?.Dispose();
            host.Dispose();
            await host.StopAsync(TimeSpan.FromSeconds(5)); // allow for graceful exit
        }

        base.OnExit(e);
    }

}