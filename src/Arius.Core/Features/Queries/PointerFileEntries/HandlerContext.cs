using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;

namespace Arius.Core.Features.Queries.PointerFileEntries;

internal record HandlerContext
{
    public required PointerFileEntriesQuery Request         { get; init; }
    public required IArchiveStorage         ArchiveStorage  { get; init; }
    public required IStateRepository        StateRepository { get; init; }
    //public required Sha256Hasher       Hasher          { get; init; }
    //public required UPath[]            Targets         { get; init; }
    public required FilePairFileSystem FileSystem      { get; init; }
    //public required DirectoryEntry     BinaryCache     { get; init; }
}