using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;

namespace Arius.Core.Repositories.StateDb;

internal record PointerFileEntryDto
{
    public byte[] BinaryHash   { get; init; }
    public string RelativePath { get; init; }
    public string Name         { get; init; }

    /// <summary>
    /// Version (in Universal Time)
    /// </summary>
    public DateTime VersionUtc { get;        init; }
    public bool      IsDeleted        { get; init; }
    public DateTime? CreationTimeUtc  { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }

    public virtual ChunkEntry Chunk { get; init; }
    [NotMapped]
    public string RelativeName => Path.Combine(RelativePath, Name);
}

internal record ChunkEntry
{
    /// <summary>
    /// The Hash of this Binary or Chunk
    /// </summary>
    public byte[] Hash { get; init; }

    /// <summary>
    /// The original/restored size of the binary or chunk
    /// </summary>
    public long OriginalLength { get; init; }

    /// <summary>
    /// The compressed size of the binary or chunk
    /// </summary>
    public long ArchivedLength { get; init; }

    /// <summary>
    /// The incremental backup size that was incurred by archiving this chunk.
    /// In case of chunked BinaryFile, this is 0
    /// </summary>
    public long IncrementalLength { get; init; }

    /// <summary>
    /// In case of a chunked BinaryFile, the number of chunks.
    /// Otherwise 1 (since we cannot know whether the chunk is a file on its own or not)
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// The AccessTier of the Chunk.
    /// Null if the ChunkEntry is for a chunked BinaryFile.
    /// </summary>
    public AccessTier? AccessTier { get; set; }

    public virtual ICollection<PointerFileEntryDto> PointerFileEntries { get; set; }
}