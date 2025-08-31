using Arius.Core.Repositories;
using Arius.Core.Services;
using Zio;

namespace Arius.Core.Commands.RestoreCommand;

internal record HandlerContext
{
    public required RestoreCommand     Request      { get; init; }
    public required IChunkStorage      ChunkStorage { get; init; }
    public required IStateRepository   StateRepo    { get; init; }
    public required Sha256Hasher       Hasher       { get; init; }
    public required UPath[]            Targets      { get; init; }
    public required FilePairFileSystem FileSystem   { get; init; }
}