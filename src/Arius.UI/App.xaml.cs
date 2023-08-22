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
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(context.Configuration, services);
            })
            .Build();
    }

    private void ConfigureServices(IConfiguration configuration, IServiceCollection services)
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

        var mainWindow = _host.Services.GetRequiredService<RepositoryChooserWindow>();
        mainWindow.Show();
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