using FluentValidation.Results;
using System;
using System.Threading.Tasks;

namespace Arius.Core.Commands;

public interface ICommandOptions
{
}

public interface ICommand<T> where T : ICommandOptions // TODO REMOVE INTERFARCE?
{
    public ValidationResult Validate(T options) => throw new NotImplementedException();
    public Task<int> ExecuteAsync(T options);
}