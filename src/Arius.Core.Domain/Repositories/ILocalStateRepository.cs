using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public enum UpsertResult
{
    Added,
    Updated,
    Unchanged
}

public interface ILocalStateRepository
{
    StateVersion Version { get; }

    IEnumerable<BinaryProperties> GetBinaryProperties();
    long                          CountBinaryProperties();
    SizeMetrics                   GetSizes();
    void                          AddBinary(BinaryProperties bp);
    bool                          BinaryExists(Hash binaryFileHash);
    void                          UpdateBinaryStorageTier(Hash hash, StorageTier effectiveTier);

    IEnumerable<PointerFileEntry> GetPointerFileEntries();
    long                          CountPointerFileEntries();
    UpsertResult                  UpsertPointerFileEntry(PointerFileEntry pfe);
    void                          DeletePointerFileEntry(PointerFileEntry pfe);


    Task<bool> UploadAsync(CancellationToken cancellationToken = default);
    void       Discard();
}