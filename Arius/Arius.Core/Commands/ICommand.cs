using FluentValidation.Results;
using System;
using System.Threading.Tasks;

namespace Arius.Core.Commands;

public interface ICommandOptions
{
}

public interface ICommand<T> where T : ICommandOptions
{
    internal IServiceProvider Services { get; }
    public ValidationResult Validate(T options) => throw new NotImplementedException();
    public Task<int> ExecuteAsync(T options);
}