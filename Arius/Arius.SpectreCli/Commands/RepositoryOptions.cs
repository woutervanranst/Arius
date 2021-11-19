using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arius.CliSpectre.Utils;
using Arius.Core.Commands;
using Arius.Core.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Arius.CliSpectre.Commands;

internal abstract class RepositoryOptions : CommandSettings, IRepositoryOptions
{
    protected RepositoryOptions(string accountName, string accountKey, string container, string passphrase, DirectoryInfo path)
    {
        // 1. Load from Environment Variables
        AccountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
        AccountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            path = new DirectoryInfo("/archive"); //when runnning in a docker container

        // 2. Try to load from config file
        // TODO Test precedence: Environment Variable < Settings < Cli
        var c = PersistedRepositoryConfigReader.LoadSettings(path, passphrase);
        if (c != default)
        {
            AccountName ??= c.accountName; // if the CLI option is not specified, AccountName will be null
            AccountKey ??= c.accountKey;
            Container ??= c.container;

            Trace.WriteLine("Loaded settings from configfile");
        }
        else
            Trace.WriteLine("Could not load settings from file");

        //3. Overwrite if manually specified
        AccountName ??= accountName;
        AccountKey ??= accountKey;
        Container ??= container;
        Passphrase ??= passphrase;

        Validate();

        // Save the Config
        PersistedRepositoryConfigReader.SaveSettings(this, path);
    }

    [Description("Blob Account Name")]
    [CommandOption("-n|--accountname <ACCOUNT_NAME>")]
    public string AccountName { get; }

    [Description("Blob Account Key")]
    [CommandOption("-k|--accountkey <ACCOUNT_KEY>")]
    public string AccountKey { get; }

    [Description("Blob Container Name")]
    [CommandOption("-c|--container <CONTAINER>")]
    public string Container { get; }

    [Description("Passphrase")]
    [CommandOption("-p|--passphrase <PASSPHRASE>")]
    public string Passphrase { get; }

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

        return base.Validate();
    }
}