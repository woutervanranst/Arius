using System.Threading.Tasks;
using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Queries.RepositoryStatistics;

internal class RepositoryStatisticsQueryHandler : AsyncQuery<RepositoryStatisticsQuery, IQueryRepositoryStatisticsResult>
{
    private readonly Repository repository;

    public RepositoryStatisticsQueryHandler(ILoggerFactory loggerFactory, Repository repository)
    {
        this.repository = repository;
    }

    protected override async Task<(QueryResultStatus Status, IQueryRepositoryStatisticsResult? Result)> ExecuteImplAsync(RepositoryStatisticsQuery options)
    {
        var s = await repository.GetStatisticsAsync();
        var r = new RepositoryStatistics
        {
            TotalSize = s.ChunkSize,
            TotalFiles = s.CurrentPointerFileEntryCount,
            TotalChunks = s.ChunkCount
        };

        return (QueryResultStatus.Success, r);
    }


    private record RepositoryStatistics : IQueryRepositoryStatisticsResult
    {
        public long TotalSize { get; init; }
        public int TotalFiles { get; init; }
        public int TotalChunks { get; init; }
    }
}