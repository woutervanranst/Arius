namespace Arius.Core.Features.Queries.PointerFileEntries;

public abstract record PointerFileEntriesQueryResult
{
}

public sealed record PointerFileEntriesQueryDirectoryResult : PointerFileEntriesQueryResult
{
    public required string RelativeName { get; init; }
}

public sealed record PointerFileEntriesQueryFileResult : PointerFileEntriesQueryResult
{
    public string? PointerFileEntry { get; init; }
    public string? PointerFileName  { get; init; }
    public string? BinaryFileName   { get; init; }

    public required long  OriginalSize { get; init; }
    public          bool? Hydrated     { get; init; } // True / False / Null in case of not present in blob
}