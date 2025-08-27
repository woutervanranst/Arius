using Arius.Core.Models;

namespace Arius.Core.Commands;

public record ArchiveCommand : RepositoryCommand
{
    public required bool          RemoveLocal   { get; init; }
    public required StorageTier   Tier          { get; init; }
    public required DirectoryInfo LocalRoot     { get; init; }

    public int Parallelism { get; init; } = 5; //Todo split up per purpose

    public int SmallFileBoundary { get; init; } = 1024 * 1024;

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}