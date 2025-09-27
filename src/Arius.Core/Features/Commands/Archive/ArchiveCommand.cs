using Arius.Core.Shared.Storage;
using Mediator;

namespace Arius.Core.Features.Commands.Archive;

public sealed record ArchiveCommand : RepositoryCommandProperties, ICommand<Unit>
{
    public required bool          RemoveLocal { get; init; }
    public required StorageTier   Tier        { get; init; }
    public required DirectoryInfo LocalRoot   { get; init; }

    public int HashingParallelism { get; init; } = Environment.ProcessorCount;
    public int UploadParallelism  { get; init; } = Math.Min(4, Environment.ProcessorCount);

    public int SmallFileBoundary { get; init; } = 1024 * 1024; // 1 MB

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}