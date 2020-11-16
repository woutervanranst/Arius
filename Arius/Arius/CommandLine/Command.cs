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

    public static class CommandHandlerExtensions
    {
        public static System.CommandLine.Invocation.ICommandHandler Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, System.Threading.Tasks.Task<int>> action) => System.CommandLine.Binding.HandlerDescriptor.FromDelegate(action).GetCommandHandler();
    }
}
