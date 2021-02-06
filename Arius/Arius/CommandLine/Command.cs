using System;
using System.CommandLine;
using System.Threading.Tasks;

namespace Arius.CommandLine
{
    internal interface IAriusCommand
    {
        Command GetCommand(ParsedCommandProvider e);
    }

    internal interface ICommandExecutor
    {
        public Task<int> Execute();
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
