namespace Arius.Core.Queries.RepositoryStatistics;

internal record RepositoryStatisticsQuery : QueryOptions
{
    public override void Validate()
    {
        // always succeeds
    }
}