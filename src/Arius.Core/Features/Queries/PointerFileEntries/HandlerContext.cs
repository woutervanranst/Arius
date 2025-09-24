using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.StateRepositories;

namespace Arius.Core.Features.Queries.PointerFileEntries;

internal record HandlerContext
{
    public required PointerFileEntriesQuery Query           { get; init; }
    public required IStateRepository        StateRepository { get; init; }
    public required FilePairFileSystem      LocalFileSystem { get; init; }
}