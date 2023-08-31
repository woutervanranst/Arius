using System;
using System.Threading.Tasks;

namespace Arius.Core.Queries;

internal interface IQueryOptions
{
    void Validate() => throw new NotImplementedException();
}

enum QueryResultStatus
{
    Success = 0,
    Failed  = -1,
    //Cancelled = -2
}

internal interface IQueryResult
{
    QueryResultStatus Status { get; }
}

internal interface IQuery<TOptions, TResults>
    where TOptions : IQueryOptions
    where TResults : IQueryResult
{
    public void Validate(TOptions options) => options.Validate();

    public TResults Execute(TOptions options);
}

internal interface IAsyncQuery<TOptions, TResults>
    where TOptions : IQueryOptions
    where TResults : IQueryResult
{
    public void Validate(TOptions options) => options.Validate();

    public Task<TResults> ExecuteAsync(TOptions options);
}