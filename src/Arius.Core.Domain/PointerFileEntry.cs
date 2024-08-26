using System.ComponentModel.DataAnnotations.Schema;

namespace Arius.Core.Domain;

internal record PointerFileEntry
{
    public byte[] HashValue { get; init; }

    [NotMapped]
    public Hash Hash => new(HashValue);

    public string RelativeName { get; init; }

    /// <summary>
    /// Version (in Universal Time)
    /// </summary>
    public DateTime VersionUtc { get; init; }

    public DateTime? CreationTimeUtc  { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }
}