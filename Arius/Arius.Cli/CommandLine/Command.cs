using System;
using System.CommandLine;

namespace Arius.Cli.CommandLine;

internal interface ICliCommand
{
    Command GetCommand();
}