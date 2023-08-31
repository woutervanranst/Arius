using System;
using System.Threading.Tasks;

namespace Arius.Core.Queries;

internal abstract record QueryOptions
{
    /// <summary>
    /// Validate these QueryOptions
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public abstract void Validate();
}

public enum QueryResultStatus
{
    Success = 0,
    Failed  = -1,
    //Cancelled = -2
}

internal interface IQueryResult
{
    QueryResultStatus Status { get; }
}

internal abstract class Query<TOptions, TResults>
    where TOptions : QueryOptions
    where TResults : IQueryResult
{
    public void Validate(TOptions options) => options.Validate();

    public TResults Execute(TOptions options)
    {
        options.Validate();

        return ExecuteImpl(options);
    }

    protected abstract TResults ExecuteImpl(TOptions options);
}

internal abstract class AsyncQuery<TOptions, TResults>
    where TOptions : QueryOptions
    where TResults : IQueryResult
{
    /// <summary>
    /// Execute the Query
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public async Task<TResults> ExecuteAsync(TOptions options)
    {
        options.Validate();

        return await ExecuteImplAsync(options);
    }
    
    protected abstract Task<TResults> ExecuteImplAsync(TOptions options);
}