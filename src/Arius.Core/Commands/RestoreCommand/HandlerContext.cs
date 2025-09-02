using Arius.Core.Hashers;
using Arius.Core.StateRepositories;
using Arius.Core.Storage;
using Zio;

namespace Arius.Core.Commands.RestoreCommand;

internal record HandlerContext
{
    public required RestoreCommand     Request         { get; init; }
    public required IArchiveStorage    ArchiveStorage  { get; init; }
    public required IStateRepository   StateRepository { get; init; }
    public required Sha256Hasher       Hasher          { get; init; }
    public required UPath[]            Targets         { get; init; }
    public required FilePairFileSystem FileSystem      { get; init; }
}