namespace Arius.Core.Features.Commands.Restore;

public sealed record RestoreCommandResult
{
    /// <summary>
    /// The total number of files in scope of the restore, after expanding the `Targets` parameter into the State repository
    /// </summary>
    public int TotalTargetFiles { get; init; }

    /// <summary>
    /// The total number of TargetFiles that already existed and were verified (no restore needed)
    /// </summary>
    public int VerifiedFilesAlreadyExisting { get; init; }


    /// <summary>
    /// The total number of chunks downloaded (regular chunks and TAR chunks)
    /// </summary>
    public int ChunksDownloaded { get; init; }

    /// <summary>
    /// The total bytes downloaded in the downloaded chunks (regular chunks and TAR chunks)
    /// </summary>
    public long BytesDownloaded { get; init; }


    /// <summary>
    /// The total number of files written to disk
    /// </summary>
    public int FilesWrittenToDisk { get; init; }

    /// <summary>
    /// The total bytes in the files written to disk
    /// </summary>
    public long BytesWrittenToDisk { get; init; }


    /// <summary>
    /// Details about the chunks that are still hydrating
    /// </summary>
    public IReadOnlyList<RehydrationDetail> Rehydrating { get; init; } = [];
}

public sealed record RehydrationDetail
{
    public required string RelativeName { get; init; }
    public required long   ArchivedSize { get; init; }
}