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

internal interface ICommand<TOptions> 
    where TOptions : CommandOptions 
{
    /// <summary>
    /// Validate these ICommandOptions
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public void Validate(TOptions options) => options.Validate();

    /// <summary>
    /// Execute the Command
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public Task<CommandResultStatus> ExecuteAsync(TOptions options);
}