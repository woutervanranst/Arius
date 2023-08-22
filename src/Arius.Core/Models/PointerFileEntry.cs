using System;
using System.IO;

namespace Arius.Core.Models;

internal record PointerFileEntry
{
    public BinaryHash BinaryHash   { get; init; }
    public string     RelativePath { get; init; }
    public string     Name         { get; init; }
    public string     RelativeName => Path.Combine(RelativePath, Name);

    /// <summary>
    /// Version (in Universal Time)
    /// </summary>
    public DateTime VersionUtc { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? CreationTimeUtc { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }

}