using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Arius.Core.Queries;

internal record RepositoryStatisticsQueryOptions : QueryOptions
{
    public override void Validate()
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


internal class RepositoryStatisticsQuery: AsyncQuery<RepositoryStatisticsQueryOptions, IQueryRepositoryStatisticsResult>
{
    private readonly Repository repository;

    public RepositoryStatisticsQuery(ILoggerFactory loggerFactory, Repository repository)
    {
        this.repository = repository;
    }

    protected override async Task<(QueryResultStatus Status, IQueryRepositoryStatisticsResult? Result)> ExecuteImplAsync(RepositoryStatisticsQueryOptions options)
    {
        var s = await repository.GetStatisticsAsync();
        var r = new RepositoryStatistics()
        {
            TotalSize   = s.ChunkSize,
            TotalFiles  = s.CurrentPointerFileEntryCount,
            TotalChunks = s.ChunkCount
        };

        return (QueryResultStatus.Success, r);
    }


    private record RepositoryStatistics : IQueryRepositoryStatisticsResult
    {
        public long TotalSize   { get; init; }
        public int  TotalFiles  { get; init; }
        public int  TotalChunks { get; init; }
    }
}