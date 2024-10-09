using System;
using System.Collections.Generic;
using System.IO;
using Arius.Core.Extensions;
using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Queries;

internal record PointerFileEntriesSubdirectoriesQueryOptions : QueryOptions
{
    public required string   Prefix     { get; init; }
    public required int      Depth      { get; init; } = 1;
    public required DateTime VersionUtc { get; init; }

    public override void Validate()
    {
        if (Path.DirectorySeparatorChar != PathExtensions.PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR && Prefix.Contains(Path.DirectorySeparatorChar))
            throw new ArgumentException($"Prefix must be platform neutral, but contains {Path.DirectorySeparatorChar}");

        if (!string.IsNullOrWhiteSpace(Prefix) && !Prefix.EndsWith(PathExtensions.PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR))
            throw new ArgumentException($"{nameof(Prefix)} needs to be String.Empty or end with a '/'");
    }
}

internal class PointerFileEntriesSubdirectoriesQuery : Query<PointerFileEntriesSubdirectoriesQueryOptions, IAsyncEnumerable<string>>
{
    private readonly ILoggerFactory loggerFactory;
    private readonly Repository repository;

    public PointerFileEntriesSubdirectoriesQuery(ILoggerFactory loggerFactory, Repository repository)
    {
        this.loggerFactory = loggerFactory;
        this.repository    = repository;
    }

    protected override (QueryResultStatus Status, IAsyncEnumerable<string>? Result) ExecuteImpl(PointerFileEntriesSubdirectoriesQueryOptions options)
    {
        var r = repository.GetPointerFileEntriesSubdirectoriesAsync(options.Prefix, options.Depth, options.VersionUtc);

        return (QueryResultStatus.Success, r);
    }
}