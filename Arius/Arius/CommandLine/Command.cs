using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    class ParsedCommandProvider
    {
        public Type CommandExecutorType { get; set; }
        public ICommandExecutorOptions CommandExecutorOptions { get; set; }
    }
}
