using System;
using System.CommandLine;

namespace Arius.CommandLine
{
    internal interface ICliCommand
    {
        Command GetCommand();
    }

    //internal class ParsedCommandProvider
    //{
    //    public Type CommandExecutorType { get; set; }
    //    public ICommandExecutorOptions CommandExecutorOptions { get; set; }
    //}
}
