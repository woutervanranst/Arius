using System;
using System.Threading.Tasks;

namespace Arius.Core.Commands;

internal abstract record CommandOptions
{
    /// <summary>
    /// Validate these CommandOptions
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public abstract void Validate();
}

public enum CommandResultStatus
{
    Success = 0,
    Error = -1,
    //Cancelled = -2
}

internal abstract class AsyncCommand<TOptions> where TOptions : CommandOptions  // TODO deprecate me
{
    /// <summary>
    /// Execute the Command
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public async Task<CommandResultStatus> ExecuteAsync(TOptions options)
    {
        options.Validate();

        return await ExecuteImplAsync(options);
    }

    protected abstract Task<CommandResultStatus> ExecuteImplAsync(TOptions options);
}