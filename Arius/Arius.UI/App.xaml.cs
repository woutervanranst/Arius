using Arius.UI.Properties;
using Arius.UI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;

namespace Arius.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IHost host;

        public static IServiceProvider ServiceProvider { get; private set; }

        public static string Name => System.Reflection.Assembly.GetEntryAssembly().GetName().Name;

        public App()
        {
            // Combination of
            // https://marcominerva.wordpress.com/2019/11/07/update-on-using-hostbuilder-dependency-injection-and-service-provider-with-net-core-3-0-wpf-applications/
            // https://marcominerva.wordpress.com/2020/01/07/using-the-mvvm-pattern-in-wpf-applications-running-on-net-core/

            host = Host.CreateDefaultBuilder()
               .ConfigureServices((context, services) =>
               {
                   ConfigureServices(context.Configuration, services);
               })

               //.ConfigureLogging()

               // https://stackoverflow.com/questions/39573571/net-core-console-application-how-to-configure-appsettings-per-environment
               // https://www.twilio.com/blog/2018/05/user-secrets-in-a-net-core-console-app.html
               // https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-5.0&tabs=windows
               //.ConfigureAppConfiguration((hostContext, builder) =>
               //{
               //    var devEnvironmentVariable = Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT");

               //    var isDevelopment = string.IsNullOrEmpty(devEnvironmentVariable) || devEnvironmentVariable.ToLower() == "development";
               //    if (isDevelopment) //(hostContext.HostingEnvironment.IsDevelopment())
               //    {
               //        builder.AddUserSecrets<App>();
               //    }
               //})
               .Build();

            ServiceProvider = host.Services;
        }

        private void ConfigureServices(IConfiguration configuration, IServiceCollection services)
        {
            //services.Configure<AppSettings>(configuration
            //    .GetSection(nameof(AppSettings)));
            //services.AddScoped<ISampleService, SampleService>();

            // Register all ViewModels.
            services.AddSingleton<MainViewModel>();

            // Register all the Windows of the applications.
            services.AddTransient<MainWindow>();


            services.AddSingleton<Facade.Facade>();

            services.AddLogging();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await host.StartAsync();


            ////https://stackoverflow.com/questions/22435561/encrypting-credentials-in-a-wpf-application
            //if (e.Args)
            //ProtectedData.Protect(data
            //var x = Settings.Default.a;

            //var window = ServiceProvider.GetRequiredService<MainWindow>();
            //window.Show();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Original code...
        }
    }

    internal class ViewModelLocator
    {
        public MainViewModel MainViewModel => App.ServiceProvider.GetRequiredService<MainViewModel>();
    }
}
