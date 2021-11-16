using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arius.Core.Services;
using Arius.SpectreCli.Utils;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Arius.SpectreCli.Commands;

public class RepositorySettings : CommandSettings // do not make it abstract as we re serializing this
{
    public RepositorySettings()
    {
        AccountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
        AccountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            Path = new DirectoryInfo("/archive");
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
    [TypeConverter(typeof(DirectoryTypeConverter))]
    [CommandArgument(0, "<PATH>")]
    public DirectoryInfo Path
    {
        get => path;
        init
        {
            path = value;

            // Load Config if it exists in the path
            var configFile = new FileInfo(System.IO.Path.Combine(path.FullName, "arius.config"));
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
    private readonly DirectoryInfo path;

    public override ValidationResult Validate()
    {
        if (AccountName is null)
            return ValidationResult.Error($"AccountName is required");

        if (AccountKey is null)
            return ValidationResult.Error($"AccountKey is required");

        if (Container is null)
            return ValidationResult.Error($"Container is required");

        if (Passphrase is null)
            return ValidationResult.Error($"Passphrase is required");


        // Save the Config
        var configFile = new FileInfo(System.IO.Path.Combine(path.FullName, "arius.config"));
        using var ms0 = new MemoryStream();
        JsonSerializer.Serialize(ms0, this);
        ms0.Seek(0, SeekOrigin.Begin);
        using var ts = configFile.OpenWrite();
        CryptoService.CompressAndEncryptAsync(ms0, ts, Passphrase).Wait();
        configFile.Attributes = FileAttributes.Hidden; // make it hidden so it is not archived by the ArchiveCommandBlocks.IndexBlock

        return base.Validate();
    }
}