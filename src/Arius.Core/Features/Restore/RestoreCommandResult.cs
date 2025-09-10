namespace Arius.Core.Features.Restore;

public sealed record RestoreCommandResult
{
    public IReadOnlyList<RehydratingDetail> Rehydrating { get; init; } = [];

    public sealed record RehydratingDetail
    {
        public required string RelativeName { get; init; }
        public required long   ArchivedSize         { get; init; }
    }
}