using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface ILocalStateRepository
{
    StateVersion Version { get; }

    IEnumerable<BinaryProperties> GetBinaryProperties();
    long                          CountBinaryProperties();
    void                          AddBinary(BinaryProperties bp);
    bool                          BinaryExists(Hash binaryFileHash);
    void                          UpdateBinaryStorageTier(Hash hash, StorageTier effectiveTier);

    IEnumerable<PointerFileEntry> GetPointerFileEntries();
    long                          CountPointerFileEntries();
    void                          AddPointerFileEntry(PointerFileEntry pfe);
    void                          DeletePointerFileEntry(PointerFileEntry pfe);

    SizeMetrics GetSizes();

    Task<bool> UploadAsync();
}