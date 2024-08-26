using System.ComponentModel.DataAnnotations.Schema;

namespace Arius.Core.Domain;

public record PointerFileEntry
{
    public byte[] HashValue { get; init; }

    [NotMapped]
    public Hash Hash => new(HashValue);

    public string RelativeName { get; init; }

    public DateTime? CreationTimeUtc  { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }
}