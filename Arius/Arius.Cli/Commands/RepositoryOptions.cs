using System.ComponentModel;
using Arius.Cli.Utils;
using Arius.Core.Commands;
using Arius.Core.Facade;
using Spectre.Console.Cli;

namespace Arius.Cli.Commands;

internal abstract class RepositoryOptions : CommandSettings, IRepositoryOptions
{
    [Description("Blob Account Name")]
    [CommandOption("-n|--accountname <ACCOUNT_NAME>")]
    public required string AccountName { get; set; } // set - not init because it needs to be (re)set in the Interceptor

    [Description("Blob Account Key")]
    [CommandOption("-k|--accountkey <ACCOUNT_KEY>")]
    [ObfuscateInLog]
    public string AccountKey { get; set; } // set - not init because it needs to be (re)set in the Interceptor

    [Description("Blob Container Name")]
    [CommandOption("-c|--container <CONTAINER>")]
    public string ContainerName { get; init; }

    [Description("Passphrase")]
    [CommandOption("-p|--passphrase <PASSPHRASE>")]
    [ObfuscateInLog]
    public string Passphrase { get; init; }
}