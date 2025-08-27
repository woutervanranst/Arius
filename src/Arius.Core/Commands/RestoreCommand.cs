namespace Arius.Core.Commands;

public record RestoreCommand : RepositoryCommand
{
    public required string[]      Targets       { get; init; }
    public required bool          Download      { get; init; }
    public required bool          IncludePointers  { get; init; }

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}
