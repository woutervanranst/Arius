using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain;

public record PointerFileEntry
{
    public static PointerFileEntry FromBinaryFileWithHash(IBinaryFileWithHash bfwh)
    {
        return new PointerFileEntry
        {
            Hash             = bfwh.Hash,
            RelativeName     = bfwh.RelativeName,
            CreationTimeUtc  = bfwh.CreationTimeUtc ?? throw new ArgumentException($"{nameof(bfwh.CreationTimeUtc)} is null"),
            LastWriteTimeUtc = bfwh.LastWriteTimeUtc ?? throw new ArgumentException($"{nameof(bfwh.LastWriteTimeUtc)} is null")
        };
    }
    public required Hash Hash { get; init; }

    public required string RelativeName { get; init; }

    public required DateTime CreationTimeUtc  { get; init; }
    public required DateTime LastWriteTimeUtc { get; init; }
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
    public required long OriginalSize { get; init; }

    /// <summary>
    /// The compressed size of the binary
    /// </summary>
    public required long ArchivedSize { get; init; }

    /// <summary>
    /// The AccessTier of the Chunk.
    /// </summary>
    public required StorageTier StorageTier { get; set; }

    //public virtual ICollection<PointerFileEntry> PointerFileEntries { get; set; }
}