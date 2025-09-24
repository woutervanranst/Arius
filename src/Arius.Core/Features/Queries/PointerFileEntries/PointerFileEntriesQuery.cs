using Mediator;

namespace Arius.Core.Features.Queries.PointerFileEntries;

public sealed record PointerFileEntriesQuery : RepositoryCommandProperties, IStreamQuery<PointerFileEntriesQueryResult>
{
    public required DirectoryInfo LocalPath { get; init; }
    public required string        Prefix    { get; init; }
}