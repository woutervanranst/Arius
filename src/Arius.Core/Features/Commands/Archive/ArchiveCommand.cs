using Arius.Core.Shared.Storage;
using Mediator;

namespace Arius.Core.Features.Commands.Archive;

public sealed record ArchiveCommand : RepositoryCommand<Unit>
{
    public required bool          RemoveLocal { get; init; }
    public required StorageTier   Tier        { get; init; }
    public required DirectoryInfo LocalRoot   { get; init; }

    public int Parallelism { get; init; } = 1; //Todo split up per purpose, use #DEBUG etc en make it cpu based

    public int SmallFileBoundary { get; init; } = 1024 * 1024; // 1 MB

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}