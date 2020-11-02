using Azure.Storage.Blobs.Models;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace Arius
{ 
public class Program
{
        public static int Main(string[] args)
        {
            var app = new CommandLineApplication
            {
                Name = "arius",
                Description = "Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier",
            };

            app.HelpOption(inherited: true);

            /*
             *  arius archive 
                    --accountname <accountname> 
                    --accountkey <accountkey> 
                    --passphrase <passphrase>
                    (--container <containername>) 
                    (--keep-local)
                    (--tier=(hot/cool/archive))
                    (--min-size=<minsizeinMB>)
                    (--simulate)
             * */

            app.Command("archive", archiveCmd =>
            {
                archiveCmd.Description = "Archive to blob";

                var accountName = archiveCmd.Option<string>("--accountname|-n <ACCOUNTNAME>", 
                    "Blob Account Name", 
                    CommandOptionType.SingleValue)
                .IsRequired();

                var accountKey = archiveCmd.Option<string>("--accountkey|-k <ACCOUNTKEY>", 
                    "Account Key", 
                    CommandOptionType.SingleValue)
                .IsRequired();

                var passphrase = archiveCmd.Option<string>("--passphrase|-p <PASSPHRASE>", 
                    "Passphrase", 
                    CommandOptionType.SingleValue)
                .IsRequired();

                var container = archiveCmd.Option<string>("--container|-c <CONTAINERNAME>", 
                    "Blob container to use. Default: 'arius'", 
                    CommandOptionType.SingleValue);

                var keepLocal = archiveCmd.Option<bool>("--keep-local",
                    "Do not delete the local copies of the file after a successful upload",
                    CommandOptionType.NoValue);

                var tier = archiveCmd.Option<string>("--tier <TIER>",
                    "Storage tier to use. Defaut: archive",
                    CommandOptionType.SingleValue)
                    .Accepts().Values("hot", "cool", "archive"); 

                var minSize = archiveCmd.Option<int>("--min-size <MB>",
                    "Minimul size of files to archive in MB. Default: 1",
                    CommandOptionType.SingleValue);

                var simulate = archiveCmd.Option<bool>("--simulate",
                    "List the differences between the local and the remote, without making any changes to remote",
                    CommandOptionType.NoValue);

                var path = archiveCmd.Argument("path", "Path to archive. Default: current directory");

                archiveCmd.OnExecute(() =>
                {
                    //CommandLineApplication.Execute<Program>()
                    //Archive()
                    //var accountName = accountName.
                    //var p = new DirectoryInfo(path.Value ?? Environment.CurrentDirectory) ;



                    Console.WriteLine($"hallo {p.FullName}");
                });
            });

            app.Command("restore", hahacmd =>
            {

            });


            //app.Command("config2", configCmd =>
            //{
            //    configCmd.OnExecute(() =>
            //    {
            //        Console.WriteLine("Specify a subcommand");
            //        configCmd.ShowHelp();
            //        return 1;
            //    });

            //    configCmd.Command("set", setCmd =>
            //    {
            //        setCmd.Description = "Set config value";
            //        var key = setCmd.Argument("key", "Name of the config").IsRequired();
            //        var val = setCmd.Argument("value", "Value of the config").IsRequired();
            //        setCmd.OnExecute(() =>
            //        {
            //            Console.WriteLine($"Setting config {key.Value} = {val.Value}");
            //        });
            //    });

            //    configCmd.Command("list", listCmd =>
            //    {
            //        var json = listCmd.Option("--json", "Json output", CommandOptionType.NoValue);
            //        listCmd.OnExecute(() =>
            //        {
            //            if (json.HasValue())
            //            {
            //                Console.WriteLine("{\"dummy\": \"value\"}");
            //            }
            //            else
            //            {
            //                Console.WriteLine("dummy = value");
            //            }
            //        });
            //    });
            //});

            app.OnExecute(() =>
            {
                Console.WriteLine("Specify a subcommand");
                app.ShowHelp();
                return 1;
            });

            return app.Execute(args);
        }

        private void Archive(string accountName, string accountKey, string passphrase, string container, bool keepLocal, AccessTier tier, int minSize, bool simulate, DirectoryInfo path)
        {

        }

        //public static void Main()
        //{
        //    var Configuration = new ConfigurationBuilder()
        //            .SetBasePath(Directory.GetCurrentDirectory())
        //            .AddJsonFile(AppDomain.CurrentDomain.BaseDirectory + "\\appsettings.json", optional: true, reloadOnChange: true)
        //            .AddEnvironmentVariables()
        //            .Build();

        //    //Log.Logger = new LoggerConfiguration()
        //    //   .ReadFrom.Configuration(Configuration)
        //    //   .Enrich.FromLogContext()
        //    //   .CreateLogger();

        //    var builder = new HostBuilder()
        //        .ConfigureServices((hostContext, services) =>
        //        {
        //            //services.AddLogging(config =>
        //            //{
        //            //    config.ClearProviders();
        //            //    config.AddProvider(new SerilogLoggerProvider(Log.Logger));
        //            //    var minimumLevel = Configuration.GetSection("Serilog:MinimumLevel")?.Value;
        //            //    if (!string.IsNullOrEmpty(minimumLevel))
        //            //    {
        //            //        config.SetMinimumLevel(Enum.Parse<LogLevel>(minimumLevel));
        //            //    }
        //            //});
        //        });

        //    //var path = @"C:\Users\Wouter\Documents\";
        //    ////var path = @"\\192.168.1.100\Video\Arius";

        //    //var passphrase = "woutervr";

        //    //// // DefaultEndpointsProtocol=https;AccountName=aurius;AccountKey=hKtsHebpvfQ9nk4UCImAgPY3Q1Pc8C2u4mFlXUCxGBkJF8Zu1ARJURjV39mymzPfpsyPeQpHAk66vy7Fs9kjvQ==;EndpointSuffix=core.windows.net

        //    //var k = new AriusCore.Arius(path, passphrase, "aurius", "hKtsHebpvfQ9nk4UCImAgPY3Q1Pc8C2u4mFlXUCxGBkJF8Zu1ARJURjV39mymzPfpsyPeQpHAk66vy7Fs9kjvQ==");

        //    //Task.Run(() => k.Monitor());

        //    //// Wait for the user to quit the program.
        //    //Console.WriteLine("Press 'q' to quit the sample.");
        //    //while (Console.Read() != 'q') ;
        //}
    }
}