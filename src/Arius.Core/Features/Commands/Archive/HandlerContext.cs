using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;

namespace Arius.Core.Features.Commands.Archive;

internal record HandlerContext
{
    public required ArchiveCommand     Request         { get; init; }
    public required IArchiveStorage    ArchiveStorage  { get; init; }
    public required StateRepository    StateRepository { get; init; }
    public required Sha256Hasher       Hasher          { get; init; }
    public required FilePairFileSystem FileSystem      { get; init; }
}