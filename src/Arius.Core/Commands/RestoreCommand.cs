using Mediator;

namespace Arius.Core.Commands;

public record RestoreCommand : ICommand
{
    public required string        AccountName   { get; init; }
    public required string        AccountKey    { get; init; }
    public required string        ContainerName { get; init; }
    public required string        Passphrase    { get; init; }
    public required string[]      Targets       { get; init; }
    public required bool          Synchronize   { get; init; }
    public required bool          Download      { get; init; }
    public required bool          KeepPointers  { get; init; }

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}
