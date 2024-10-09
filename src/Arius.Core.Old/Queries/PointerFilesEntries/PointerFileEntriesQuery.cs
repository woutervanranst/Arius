namespace Arius.Core.Queries.PointerFilesEntries;

internal record PointerFileEntriesQuery : QueryOptions
{
    public string? RelativeDirectory { get; init; } = null;

    public override void Validate()
    {
        // Always succeeds
    }
}