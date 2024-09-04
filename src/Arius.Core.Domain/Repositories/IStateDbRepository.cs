using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IStateDbRepository
{
    RepositoryVersion                  Version { get; }
    IAsyncEnumerable<PointerFileEntry> GetPointerFileEntries();
    IAsyncEnumerable<BinaryProperties> GetBinaryProperties();
    bool                               BinaryExists(Hash binaryFileHash);
}