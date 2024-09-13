using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface ILocalStateRepository
{
    RepositoryVersion Version    { get; }
    bool              HasChanges { get; }
    void              Vacuum();


    IEnumerable<BinaryProperties> GetBinaryProperties();
    long                          CountBinaryProperties();
    long                          GetArchiveSize();
    void                          AddBinary(BinaryProperties bp);
    bool                          BinaryExists(Hash binaryFileHash);
    void                          UpdateBinaryStorageTier(Hash hash, StorageTier effectiveTier);

    IEnumerable<PointerFileEntry> GetPointerFileEntries();
    long                          CountPointerFileEntries();
    void                          AddPointerFileEntry(PointerFileEntry pfe);
    void                          DeletePointerFileEntry(PointerFileEntry pfe);
}