namespace Arius.Core.Queries.RepositoryStatistics;

internal record RepositoryStatisticsQueryOptions : QueryOptions
{
    public override void Validate()
    {
        // always succeeds
    }
}