using Mediator;

namespace Arius.Core.Features.Commands.Restore;

public sealed record RestoreCommand : RepositoryCommandProperties, ICommand<RestoreCommandResult>
{
    public required DirectoryInfo LocalRoot       { get; init; }
    public required string[]      Targets         { get; init; }
    public required bool          Download        { get; init; }
    public required bool          IncludePointers { get; init; }

    public int HashParallelism     { get; init; } = Environment.ProcessorCount;
    public int DownloadParallelism { get; init; } = Math.Min(4, Environment.ProcessorCount);

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }

    public Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision> RehydrationQuestionHandler { get; init; } = _ => RehydrationDecision.StandardPriority;
}

public enum RehydrationDecision
{
    StandardPriority,
    HighPriority,
    DoNotRehydrate
}