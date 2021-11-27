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
    protected RepositoryOptions(ILogger<RepositoryOptions> logger, string accountName, string accountKey, string container, string passphrase, DirectoryInfo path)
    {
        this.logger = logger;

        // 1. Load from Environment Variables
        AccountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME"); //TODO check https://github.com/spectreconsole/spectre.console/issues/539
        AccountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            if (path is not null)
                throw new InvalidOperationException("DOTNET_RUNNING_IN_CONTAINER is true but PATH argument is specified");

            Path = new DirectoryInfo("/archive"); //when runnning in a docker container

            logger.LogWarning($"{((DirectoryInfo)Path).FullName} {((DirectoryInfo)Path).Exists}");
        }

        // 2. Try to load from config file
        // TODO Test precedence: Environment Variable < Settings < Cli
        var c = PersistedRepositoryConfigReader.LoadSettings(path, passphrase);
        if (c != default)
        {
            AccountName ??= c.accountName; // if the CLI option is not specified, AccountName will be null
            AccountKey ??= c.accountKey;
            Container ??= c.container;

            logger.LogDebug("Loaded options from configfile");
        }
        else
            logger.LogDebug("Could not load options from file");

        //3. Overwrite if manually specified
        if (accountName is not null)
            AccountName = accountName;

        if (accountKey is not null)
            AccountKey = accountKey;

        if (container is not null)
            Container = container;

        if (passphrase is not null)
            Passphrase = passphrase;

        if (path is not null)
            Path = path;
    }

    private readonly ILogger<RepositoryOptions> logger;

    [Description("Blob Account Name")]
    [CommandOption("-n|--accountname <ACCOUNT_NAME>")]
    public string AccountName { get; }

    [Description("Blob Account Key")]
    [CommandOption("-k|--accountkey <ACCOUNT_KEY>")]
    [ObfuscateInLog]
    public string AccountKey { get; }

    [Description("Blob Container Name")]
    [CommandOption("-c|--container <CONTAINER>")]
    public string Container { get; }

    [Description("Passphrase")]
    [CommandOption("-p|--passphrase <PASSPHRASE>")]
    [ObfuscateInLog]
    public string Passphrase { get; }

    protected object Path { get; }

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

        if (Path is null)
            return ValidationResult.Error($"Path is required");

        // Save the Config
        if (Path is DirectoryInfo di)
        {
            logger.LogDebug("Saving options");
            PersistedRepositoryConfigReader.SaveSettings(logger, this, di);
        }
        else
        {
            logger.LogDebug("Path is not a directory, not saving options");
        }

        return base.Validate();
    }
}