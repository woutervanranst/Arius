using System;
using System.IO;
using Arius.Core.Extensions;

namespace Arius.Core.Queries.PointerFileEntriesSubdirectories;

internal record PointerFileEntriesSubdirectoriesQuery : QueryOptions
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