using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Repositories;

public interface IStateDbRepository
{
    RepositoryVersion                  Version { get; }
    IAsyncEnumerable<PointerFileEntry> GetPointerFileEntries();
    IAsyncEnumerable<BinaryProperties> GetBinaryProperties();
    void                               AddBinary(BinaryProperties bp);
    bool                               BinaryExists(Hash binaryFileHash);
    bool                               BinaryExists(IFileWithHash f);
}