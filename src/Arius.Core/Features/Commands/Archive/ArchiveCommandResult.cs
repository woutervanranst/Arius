namespace Arius.Core.Features.Commands.Archive;

public sealed record ArchiveCommandResult
{
    public int  FilesIndexed    { get; init; }
    public int  FilesUploaded   { get; init; }
    public int  FilesSkipped    { get; init; }
    public long BytesOriginal   { get; init; }
    public long BytesArchived   { get; init; }
    public bool StateUploaded   { get; init; }
}
