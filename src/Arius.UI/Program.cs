using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Arius.UI
{
    public class Program
    {
        [STAThread]
        public static void Main()
        {
            /* Based on https://blog.elmah.io/logging-and-global-error-handling-in-net-7-wpf-applications/
               and https://www.meziantou.net/creating-a-custom-main-method-in-a-wpf-application.htm */

            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<App>();
                    services.AddSingleton<RepositoryExplorer>();
                })
                .Build();

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                // unhandled exceptions
            };

            var app = host.Services.GetRequiredService<App>();
            app.Run();
        }
    }
}
