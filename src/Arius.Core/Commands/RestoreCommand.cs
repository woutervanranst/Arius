namespace Arius.Core.Commands;

public record RestoreCommand : RepositoryCommand
{
    public required DirectoryInfo LocalRoot       { get; init; }
    public required string[]      Targets         { get; init; }
    public required bool          Download        { get; init; }
    public required bool          IncludePointers { get; init; }

    public int DownloadParallelism { get; init; } = 1;

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}