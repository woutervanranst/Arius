using Arius.Core.Storage;

namespace Arius.Core.Commands.ArchiveCommand;

public record ArchiveCommand : RepositoryCommand
{
    public required bool          RemoveLocal   { get; init; }
    public required StorageTier   Tier          { get; init; }
    public required DirectoryInfo LocalRoot     { get; init; }

    public int Parallelism { get; init; } = 1; //Todo split up per purpose, use #DEBUG etc en make it cpu based

    public int SmallFileBoundary { get; init; } = 1024 * 1024;

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}