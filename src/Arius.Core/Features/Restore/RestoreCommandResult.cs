namespace Arius.Core.Features.Restore;

public sealed record RestoreCommandResult
{
    public IReadOnlyList<RehydrationDetail> Rehydrating { get; init; } = [];
}

public sealed record RehydrationDetail
    {
    public required string RelativeName { get; init; }
    public required long   ArchivedSize { get; init; }
}