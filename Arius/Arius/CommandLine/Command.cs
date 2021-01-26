using System;
using System.CommandLine;

namespace Arius.CommandLine
{
    internal interface IAriusCommand
    {
        Command GetCommand(ParsedCommandProvider e);
    }

    internal interface ICommandExecutor
    {
        public int Execute();
    }

    internal interface ICommandExecutorOptions
    {
    }

    internal class ParsedCommandProvider
    {
        public Type CommandExecutorType { get; set; }
        public ICommandExecutorOptions CommandExecutorOptions { get; set; }
    }
}
