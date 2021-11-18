using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arius.CliSpectre.Utils;
using Arius.Core.Commands;
using Arius.Core.Services;
using Arius.SpectreCli.Utils;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Arius.CliSpectre.Commands;

internal abstract class RepositoryOptions : CommandSettings, IRepositoryOptions
{
    protected RepositoryOptions()
    {
        AccountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
        AccountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            Path = new DirectoryInfo("/archive"); //when runnning in a docker container
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
            // TODO Test precedence: Environment Variable < Settings < Cli
            var c = PersistedRepositoryConfigReader.LoadSettings(value, Passphrase);

            if (c != default)
            {
                AccountName ??= c.accountName; // if the CLI option is not specified, AccountName will be null
                AccountKey ??= c.accountKey;
                Container ??= c.container;
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
        PersistedRepositoryConfigReader.SaveSettings(this);

        return base.Validate();
    }
}