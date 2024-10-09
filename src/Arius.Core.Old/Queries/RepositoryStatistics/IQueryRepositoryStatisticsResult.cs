namespace Arius.Core.Queries.RepositoryStatistics;

public interface IQueryRepositoryStatisticsResult
{
    public long TotalSize   { get; }
    public int  TotalFiles  { get; }
    public int  TotalChunks { get; }
}