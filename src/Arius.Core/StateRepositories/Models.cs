using Arius.Core.Models;

namespace Arius.Core.StateRepositories;

internal record BinaryProperties
{
    public         Hash                          Hash               { get; init; }
    public         Hash?                         ParentHash         { get; init; }
    public         long                          OriginalSize       { get; init; }
    public         long?                         ArchivedSize       { get; init; } // null in case of tarred archives
    public         StorageTier?                  StorageTier        { get; set; } // settable in case of tarred archives
    public virtual ICollection<PointerFileEntry> PointerFileEntries { get; set; }
}

internal record PointerFileEntry
{
    public         Hash             Hash             { get; init; }
    public         string           RelativeName     { get; init; }
    public         DateTime?        CreationTimeUtc  { get; set; }
    public         DateTime?        LastWriteTimeUtc { get; set; }
    public virtual BinaryProperties BinaryProperties { get; init; }
}