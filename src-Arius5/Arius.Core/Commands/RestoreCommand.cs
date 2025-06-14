using System.IO;
using Arius.Core.Models;

namespace Arius.Core.Commands;

public record RestoreCommand
{
    public required string        AccountName   { get; init; }
    public required string        AccountKey    { get; init; }
    public required string        ContainerName { get; init; }
    public required string        Passphrase    { get; init; }
    public required DirectoryInfo LocalRoot     { get; init; }
    public required bool          Synchronize   { get; init; }
    public required bool          Download      { get; init; }
    public required bool          KeepPointers  { get; init; }

    public IProgress<ProgressUpdate>? ProgressReporter { get; init; }
}
