using System.Configuration;
using System.Data;
using System.Windows;
using Arius.Core.Facade;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.UI;

public partial class App : System.Windows.Application
{
    private readonly IHost                    host;
    private          RepositoryChooserWindow  chooserWindow;
    private          RepositoryExplorerWindow explorerWindow;

    public App()
    {
        host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        var messenger = host.Services.GetRequiredService<IMessenger>();
        messenger.Register<RepositoryChosenMessage>(this, OnRepositoryChosen);
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
        chooserWindow             = new RepositoryChooserWindow();
        chooserWindow.DataContext = host.Services.GetRequiredService<ChooseRepositoryViewModel>();
        chooserWindow.Show();
    }

    private void OnRepositoryChosen(object sender, RepositoryChosenMessage message)
    {
        chooserWindow.Close();

        explorerWindow = new RepositoryExplorerWindow();
        var viewModel = host.Services.GetRequiredService<ExploreRepositoryViewModel>();
        viewModel.SetRepository(message.ChosenRepository); // Pass the chosen repository to the ViewModel

        explorerWindow.DataContext = viewModel;
        explorerWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (host)
        {
            //_host.Dispose();
            await host.StopAsync(TimeSpan.FromSeconds(5)); // allow for graceful exit
        }

        base.OnExit(e);
    }

}