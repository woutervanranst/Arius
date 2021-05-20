using System;
using System.CommandLine;

namespace Arius.CommandLine
{
    internal interface IAriusCommand
    {
        Command GetCommand(ParsedCommandProvider pcp);
    }

    internal class ParsedCommandProvider
    {
        public Type CommandExecutorType { get; set; }
        public ICommandExecutorOptions CommandExecutorOptions { get; set; }
    }
}
