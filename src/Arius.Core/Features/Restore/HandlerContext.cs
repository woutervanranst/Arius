using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Zio;

namespace Arius.Core.Features.Restore;

internal record HandlerContext
{
    public required RestoreCommand     Request         { get; init; }
    public required IArchiveStorage    ArchiveStorage  { get; init; }
    public required IStateRepository   StateRepository { get; init; }
    public required Sha256Hasher       Hasher          { get; init; }
    public required UPath[]            Targets         { get; init; }
    public required FilePairFileSystem FileSystem      { get; init; }
    public required UPath              BinaryCache     { get; init; }
}