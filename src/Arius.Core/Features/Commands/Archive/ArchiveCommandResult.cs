namespace Arius.Core.Features.Commands.Archive;

public sealed record ArchiveCommandResult
{
    // Before

    /// <summary>
    /// The total number of local files indexed during the archive operation
    /// </summary>
    public int TotalLocalFiles { get; init; }

    /// <summary>
    /// The number of pointer files that already existed on disk before this archive operation
    /// </summary>
    public int ExistingPointerFiles { get; init; }


    // Operation

    /// <summary>
    /// The total number of unique binaries (files) uploaded (includes individual files in TAR archives)
    /// </summary>
    public int UniqueBinariesUploaded { get; init; }

    /// <summary>
    /// The total number of unique chunks (blobs) uploaded to storage.
    /// This equals the number of large file uploads plus the number of TAR archives uploaded.
    /// </summary>
    public int UniqueChunksUploaded { get; init; }

    /// <summary>
    /// The uncompressed size (in bytes) of unique files uploaded during this operation
    /// </summary>
    public long BytesUploadedUncompressed { get; init; }

    /// <summary>
    /// The compressed size (in bytes) of unique files uploaded to Azure Blob Storage (after compression and encryption)
    /// </summary>
    public long BytesUploadedCompressed { get; init; }

    /// <summary>
    /// The number of new pointer files written to disk during this archive operation
    /// </summary>
    public int PointerFilesCreated { get; init; }

    /// <summary>
    /// The number of pointer file entries deleted (because they no longer exist on disk)
    /// </summary>
    public int PointerFileEntriesDeleted { get; init; }

    /// <summary>
    /// The name of the new state file that was uploaded, or null if no state changes occurred
    /// </summary>
    public string? NewStateName { get; init; }
}
