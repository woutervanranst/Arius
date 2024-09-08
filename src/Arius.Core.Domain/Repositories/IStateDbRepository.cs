using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IStateDbRepository
{
    RepositoryVersion Version { get; }


    
    //IEnumerable<BinaryProperties> GetBinaryProperties();
    long CountPointerFileEntries();
    long CountBinaryProperties();
    void AddBinary(BinaryProperties bp);
    bool BinaryExists(Hash binaryFileHash);

    IEnumerable<PointerFileEntry> GetPointerFileEntries();

    void AddPointerFileEntry(PointerFileEntry pfe);
    void DeletePointerFileEntry(PointerFileEntry pfe);
}