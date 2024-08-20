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
    Error  = -1,
    //Cancelled = -2
}

internal abstract class Query<TOptions, TResult> where TOptions : QueryOptions // TODO deprecate me
{
    /// <summary>
    /// Execute the Query
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public (QueryResultStatus Status, TResult? Result) Execute(TOptions options)
    {
        options.Validate();

        return ExecuteImpl(options);
    }

    protected abstract (QueryResultStatus Status, TResult? Result) ExecuteImpl(TOptions options);
}

internal abstract class AsyncQuery<TOptions, TResult> where TOptions : QueryOptions
{
    /// <summary>
    /// Execute the Query
    /// </summary>
    /// <exception cref="ArgumentException">Throws an ArgumentException if the options are not valid</exception>
    public async Task<(QueryResultStatus Status, TResult? Result)> ExecuteAsync(TOptions options)
    {
        options.Validate();

        return await ExecuteImplAsync(options);
    }
    
    protected abstract Task<(QueryResultStatus Status, TResult? Result)> ExecuteImplAsync(TOptions options);
}