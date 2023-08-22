using System.Configuration;
using System.Data;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arius.UI;

public partial class App : Application
{
    private readonly IHost                    _host;
    private          RepositoryChooserWindow  _chooserWindow;
    private          RepositoryExplorerWindow _explorerWindow;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        var messenger = _host.Services.GetRequiredService<IMessenger>();
        messenger.Register<RepositoryChosenMessage>(this, OnRepositoryChosen);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configure your services here

        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        
        services.AddSingleton<IExternalFacade, ExternalFacade>();
        
        services.AddTransient<ChooseRepositoryViewModel>();
        services.AddTransient<ExploreRepositoryViewModel>();

        services.AddTransient<RepositoryChooserWindow>();
        services.AddTransient<RepositoryExplorerWindow>();

    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();
        _chooserWindow             = new RepositoryChooserWindow();
        _chooserWindow.DataContext = _host.Services.GetRequiredService<ChooseRepositoryViewModel>();
        _chooserWindow.Show();
    }

    private void OnRepositoryChosen(RepositoryChosenMessage message)
    {
        _chooserWindow.Close();

        _explorerWindow = new RepositoryExplorerWindow();
        var viewModel = _host.Services.GetRequiredService<ExploreRepositoryViewModel>();
        viewModel.SetRepository(message.ChosenRepository); // Pass the chosen repository to the ViewModel

        _explorerWindow.DataContext = viewModel;
        _explorerWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (_host)
        {
            //_host.Dispose();
            await _host.StopAsync(TimeSpan.FromSeconds(5)); // allow for graceful exit
        }

        base.OnExit(e);
    }

}