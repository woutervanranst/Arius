namespace Arius.Core.Queries.ContainerNames;

internal record ContainerNamesQuery : QueryOptions
{
    public required int MaxRetries { get; init; }

    public override void Validate()
    {
        // Always succeeds
    }
}