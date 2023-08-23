using System;
using System.IO;

namespace Arius.Core.Models;

internal record PointerFileEntry
{
    public BinaryHash BinaryHash         { get; init; }

    public string     RelativeParentPath { get; init; }
    public string     DirectoryName      { get; init; }
    public string     Name               { get; init; }
    public string     RelativeName       => Path.Combine(RelativeParentPath, DirectoryName, Name);

    /// <summary>
    /// Version (in Universal Time)
    /// </summary>
    public DateTime VersionUtc { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? CreationTimeUtc { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }

}