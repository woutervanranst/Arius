namespace Arius.Core.Models;

internal record BinaryProperties
{
    public BinaryHash Hash { get; init; }
    public long OriginalLength { get; init; }
    public long ArchivedLength { get; init; }
    public long IncrementalLength { get; init; }
    public int ChunkCount { get; init; }
}