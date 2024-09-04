using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain;

public record PointerFileEntry
{
    public Hash Hash { get; init; }

    public string RelativeName { get; init; }

    public DateTime? CreationTimeUtc  { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }
}

public record BinaryProperties
{
    /// <summary>
    /// The Hash of this Binary
    /// </summary>
    public Hash Hash { get; init; }

    /// <summary>
    /// The original/restored size of the binary
    /// </summary>
    public long OriginalLength { get; init; }

    /// <summary>
    /// The compressed size of the binary
    /// </summary>
    public long ArchivedLength { get; init; }

    /// <summary>
    /// The incremental backup size that was incurred by archiving this chunk.
    /// </summary>
    public long IncrementalLength { get; init; }

    /// <summary>
    /// The AccessTier of the Chunk.
    /// </summary>
    public StorageTier StorageTier { get; set; }

    //public virtual ICollection<PointerFileEntry> PointerFileEntries { get; set; }
}