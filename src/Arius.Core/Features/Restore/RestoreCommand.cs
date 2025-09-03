namespace Arius.Core.Features.Restore;

public sealed record RestoreCommand : RepositoryCommand
{
    public required DirectoryInfo LocalRoot       { get; init; }
    public required string[]      Targets         { get; init; }
    public required bool          Download        { get; init; }
    public required bool          IncludePointers { get; init; }

    public int HashParallelism { get; init; } = 1; // TODO if #DEBUG en based on CPU count?
    public int DownloadParallelism { get; init; } = 1; // TODO if #DEBUG en based on CPU count?

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}