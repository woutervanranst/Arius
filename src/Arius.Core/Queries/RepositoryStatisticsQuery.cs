using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Arius.Core.Queries;

internal record RepositoryStatisticsQueryOptions : IQueryOptions
{
    public void Validate()
    {
        // always succeeds
    }
}


public interface IQueryRepositoryStatisticsResult
{
    public long TotalSize   { get; }
    public int  TotalFiles  { get; }
    public int  TotalChunks { get; }
}

internal record RepositoryStatisticsQueryResult : IQueryResult
{
    public QueryResultStatus Status { get; init; }


    internal record RepositoryStatistics : IQueryRepositoryStatisticsResult
    {
        public long TotalSize   { get; init; }
        public int  TotalFiles  { get; init; }
        public int  TotalChunks { get; init; }
    }
    public IQueryRepositoryStatisticsResult? Result { get; init; }
}


internal class RepositoryStatisticsQuery: IAsyncQuery<RepositoryStatisticsQueryOptions, RepositoryStatisticsQueryResult>
{
    private readonly Repository repository;

    public RepositoryStatisticsQuery(ILoggerFactory loggerFactory, Repository repository)
    {
        this.repository = repository;
    }

    public async Task<RepositoryStatisticsQueryResult> ExecuteAsync(RepositoryStatisticsQueryOptions options)
    {
        var s = await repository.GetStatisticsAsync();
        var r = new RepositoryStatisticsQueryResult.RepositoryStatistics()
        {
            TotalSize   = s.ChunkSize,
            TotalFiles  = s.CurrentPointerFileEntryCount,
            TotalChunks = s.ChunkCount
        };

        return new RepositoryStatisticsQueryResult { Status = QueryResultStatus.Success, Result = r };
    }
}