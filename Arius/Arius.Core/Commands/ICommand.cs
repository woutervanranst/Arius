using System;
using System.Threading.Tasks;

namespace Arius.Core.Commands;

public interface ICommandOptions
{
}

public interface ICommand<T> where T : ICommandOptions
{
    internal IServiceProvider Services { get; }
    public Task<int> ExecuteAsync(T options);
}