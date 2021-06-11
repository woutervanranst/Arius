using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Cli.Extensions
{
    // See https://github.com/dotnet/command-line-api/issues/796#issuecomment-689738660
    internal static class CommandHandlerExtensions
    {
        /// <summary>
        /// Workaround for <see href="https://github.com/dotnet/command-line-api/issues/796"/>.
        /// </summary>
        public static ICommandHandler WithExceptionHandler(this ICommandHandler commandHandler, Func<Exception, int> exceptionHandler)
        {
            return new ExceptionHandlingCommandHandler(commandHandler, exceptionHandler);
        }

        private sealed class ExceptionHandlingCommandHandler : ICommandHandler
        {
            private readonly ICommandHandler commandHandler;
            private readonly Func<Exception, int> exceptionHandler;

            public ExceptionHandlingCommandHandler(ICommandHandler commandHandler, Func<Exception, int> exceptionHandler)
            {
                this.commandHandler = commandHandler;
                this.exceptionHandler = exceptionHandler;
            }

            public async Task<int> InvokeAsync(InvocationContext context)
            {
                try
                {
                    return await commandHandler.InvokeAsync(context);
                }
                catch (Exception ex)
                {
                    return exceptionHandler.Invoke(ex);
                }
            }
        }
    }
}
