using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arius.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Spectre.Console.Examples;

namespace Arius.SpectreCli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var registrations = new ServiceCollection();
            registrations.AddSingleton<IGreeter, HelloWorldGreeter>();
            //registrations.AddLogging();
            //registrations.AddSingleton<ILogger<ArchiveCommand>>((Logger<ArchiveCommand>)null);

            // Create a type registrar and register any dependencies.
            // A type registrar is an adapter for a DI framework.
            var registrar = new TypeRegistrar(registrations);

            var app = new CommandApp(registrar);
            app.Configure(config =>
            {
                config.SetApplicationName("arius");

                config.AddCommand<ArchiveCommand>("archive");

                //config.AddBranch<ArchiveSettings>("archive", add =>
                //{
                //    add.AddCommand<AddPackageCommand>("package");
                //    add.AddCommand<AddReferenceCommand>("reference");
                //});
            });

            return app.Run(args);
        }
    }


    public interface IGreeter
    {
        void Greet(string name);
    }
    public sealed class HelloWorldGreeter : IGreeter
    {
        public void Greet(string name)
        {
            //AnsiConsole.WriteLine($"Hello {name}!");
        }
    }

    public abstract class RepositorySettings : CommandSettings
    {
        protected RepositorySettings()
        {
            AccountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
            AccountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");

            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
                Path = "/archive";
        }

        [Description("Blob Account Name")]
        [CommandOption("-n|--accountname <ACCOUNT_NAME>")]
        public string AccountName { get; init; }

        [Description("Blob Account Key")]
        [CommandOption("-k|--accountkey <ACCOUNT_KEY>")]
        public string AccountKey { get; init; }

        [Description("Blob Container Name")]
        [CommandOption("-c|--container <CONTAINER>")]
        public string Container { get; init; }

        [Description("Passphrase")]
        [CommandOption("-p|--passphrase <PASSPHRASE>")]
        public string Passphrase { get; set; }

        [JsonIgnore]
        [Description("Path to archive")]
        [CommandArgument(0, "<PATH>")]
        public string Path
        {
            get => path;
            init
            {
                path = value;

                // Load Config if it exists in the path
                var configFile = new FileInfo(System.IO.Path.Combine(path, "arius.config"));
                try
                {
                    if (configFile.Exists)
                    {
                        using var ss = configFile.OpenRead();
                        using var ms = new MemoryStream();
                        CryptoService.DecryptAndDecompressAsync(ss, ms, Passphrase).Wait();
                        ms.Seek(0, SeekOrigin.Begin);
                        var s = JsonSerializer.Deserialize<RepositorySettings>(ms);

                        this.AccountName = s.AccountName;
                        this.AccountKey = s.AccountKey;
                        this.Container = s.Container;
                    }
                }
                catch (AggregateException e) when (e.InnerException is InvalidDataException)
                {
                    // Wrong Passphrase?
                }
                catch (AggregateException e) when (e.InnerException is ArgumentNullException)
                {
                    // No passphrase
                }
                catch (JsonException e)
                {
                    configFile.Delete();
                    //Console.WriteLine(e);
                    //throw;
                }
            }
        }
        private readonly string path;


        public override Spectre.Console.ValidationResult Validate()
        {
            if (AccountName is null)
                return Spectre.Console.ValidationResult.Error($"AccountName is required");

            if (AccountKey is null)
                return Spectre.Console.ValidationResult.Error($"AccountKey is required");

            if (Container is null)
                return Spectre.Console.ValidationResult.Error($"Container is required");

            if (Passphrase is null)
                return Spectre.Console.ValidationResult.Error($"Passphrase is required");


            // Save the Config
            var configFile = new FileInfo(System.IO.Path.Combine(path, "arius.config"));
            using var ms0 = new MemoryStream();
            JsonSerializer.Serialize(ms0, this);
            ms0.Seek(0, SeekOrigin.Begin);
            using var ts = configFile.OpenWrite();
            CryptoService.CompressAndEncryptAsync(ms0, ts, Passphrase).Wait();
            configFile.Attributes = FileAttributes.Hidden; // make it hidden so it is not archived by the ArchiveCommandBlocks.IndexBlock

            return base.Validate();
        }
    }

    

    public class ArchiveCommand : Command<ArchiveCommand.ArchiveSettings>
    {
        public class ArchiveSettings : RepositorySettings
        {
            [Description("Storage tier to use (hot|cool|archive)")]
            [CommandOption("-t|--tier <TIER>")]
            [DefaultValue("archive")]
            public string Tier { get; set; }

            [Description("Remove local file after a successful upload")]
            [CommandOption("--remove-local")]
            [DefaultValue(false)]
            public bool RemoveLocal { get; set; }

            [Description("Deduplicate the chunks in the binary files")]
            [CommandOption("--dedup")]
            [DefaultValue(false)]
            public bool Dedup { get; set; }

            [Description("Use the cached hash of a file (faster, do not use in an archive where file contents change)")]
            [CommandOption("--fasthash")]
            [DefaultValue(false)]
            public bool Fasthash { get; set; }


            public override Spectre.Console.ValidationResult Validate()
            {
                if (Tier is null)
                    return Spectre.Console.ValidationResult.Error($"Tier is required");

                string[] validTiers = { "hot", "cool", "archive" };
                Tier = Tier.ToLowerInvariant();
                if (!validTiers.Contains(Tier))
                    return Spectre.Console.ValidationResult.Error($"'{Tier}' is not a valid tier");

                //if (!File.GetAttributes(PathString).HasFlag(FileAttributes.Directory))
                //    return ValidationResult.Error($"'{PathString}' is not a valid directory");

                //if (!Directory.Exists(PathString) || !File.GetAttributes(PathString).HasFlag(FileAttributes.Directory)) // as per https://stackoverflow.com/a/1395226/1582323
                //    return ValidationResult.Error($"'{PathString}' is not a valid directory");

                return base.Validate();
            }
        }



        public ArchiveCommand(IGreeter /*ILogger<ArchiveCommand>*/ logger)
        {
        }

        public override int Execute(CommandContext context, ArchiveSettings settings)
        {
            Console.WriteLine(settings.Path);
            // Omitted
            return 0;
        }

        //public override ValidationResult Validate(CommandContext context, ArchiveSettings settings)
        //{
        //    if (settings.Project is null)
        //        return ValidationResult.Error($"Path not found");
            
        //    return base.Validate(context, settings);
        //}
    }
}