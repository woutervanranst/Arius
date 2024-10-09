using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Arius.Core.Models;

internal record PointerFileEntry
{
    public byte[]     BinaryHashValue { get; init; }

    [NotMapped]
    public BinaryHash BinaryHash      => new (BinaryHashValue);

    public string RelativeName { get; init; }

    /// <summary>
    /// Version (in Universal Time)
    /// </summary>
    public DateTime VersionUtc { get; init; }

    public bool      IsDeleted        { get; init; }
    public DateTime? CreationTimeUtc  { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }

    public virtual ChunkEntry Chunk { get; init; }
}