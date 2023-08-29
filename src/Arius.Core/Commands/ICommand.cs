using System;
using System.Threading.Tasks;

namespace Arius.Core.Commands;

internal interface ICommandOptions
{
    /// <summary>
    /// Validate these ICommandOptions
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    void Validate() => throw new NotImplementedException();
}

public enum CommandResultStatus
{
    Success = 0,
    Error = -1,
    //Cancelled = -2
}

internal interface ICommand<TOptions> 
    where TOptions : ICommandOptions 
{
    /// <summary>
    /// Validate these ICommandOptions
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public void      Validate(TOptions options) => options.Validate();

    /// <summary>
    /// Execute the Command
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public Task<CommandResultStatus> ExecuteAsync(TOptions options);
}