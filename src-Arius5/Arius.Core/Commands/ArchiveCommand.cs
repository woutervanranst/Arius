using Arius.Core.Models;
using MediatR;

namespace Arius.Core.Commands;

public record ArchiveCommand : IRequest
{
    public required string        AccountName   { get; init; }
    public required string        AccountKey    { get; init; }
    public required string        ContainerName { get; init; }
    public required string        Passphrase    { get; init; }
    public required bool          RemoveLocal   { get; init; }
    public required StorageTier   Tier          { get; init; }
    public required DirectoryInfo LocalRoot     { get; init; }

    public int Parallelism { get; init; } = 10;

    public int SmallFileBoundary { get; init; } = 2 * 1024 * 1024;

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}