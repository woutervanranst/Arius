using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain;

public record PointerFileEntry
{
    public static PointerFileEntry FromBinaryFile(BinaryFileWithHash bf)
    {
        return new PointerFileEntry
        {
            Hash             = bf.Hash,
            RelativeName     = bf.RelativeName,
            CreationTimeUtc  = bf.CreationTimeUtc,
            LastWriteTimeUtc = bf.LastWriteTimeUtc
        };
    }
    public required Hash Hash { get; init; }

    public required string RelativeName { get; init; }

    public required DateTime? CreationTimeUtc  { get; init; }
    public required DateTime? LastWriteTimeUtc { get; init; }
}

public record BinaryProperties
{
    /// <summary>
    /// The Hash of this Binary
    /// </summary>
    public required Hash Hash { get; init; }

    /// <summary>
    /// The original/restored size of the binary
    /// </summary>
    public required long OriginalLength { get; init; }

    /// <summary>
    /// The compressed size of the binary
    /// </summary>
    public required long ArchivedLength { get; init; }

    /// <summary>
    /// The incremental backup size that was incurred by archiving this chunk.
    /// </summary>
    public required long IncrementalLength { get; init; }

    /// <summary>
    /// The AccessTier of the Chunk.
    /// </summary>
    public required StorageTier StorageTier { get; set; }

    //public virtual ICollection<PointerFileEntry> PointerFileEntries { get; set; }
}