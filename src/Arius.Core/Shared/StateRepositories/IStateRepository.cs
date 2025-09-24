using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Zio;

namespace Arius.Core.Shared.StateRepositories;

internal interface IStateRepository
{
    FileEntry                         StateDatabaseFile { get; }
    bool                              HasChanges        { get; }
    void                              Vacuum();
    void                              Delete();
    BinaryProperties?                 GetBinaryProperty(Hash h);
    void                              SetBinaryPropertyArchiveTier(Hash h, StorageTier tier);
    void                              AddBinaryProperties(params BinaryProperties[] bps);
    void                              UpsertPointerFileEntries(params PointerFileEntry[] pfes);
    IEnumerable<PointerFileDirectory> GetPointerFileDirectories(string relativeNamePrefix, bool topDirectoryOnly);
    IEnumerable<PointerFileEntry>     GetPointerFileEntries(string relativeNamePrefix, bool topDirectoryOnly, bool includeBinaryProperties = false);
    PointerFileEntry?                 GetPointerFileEntry(string relativeName, bool includeBinaryProperties = false);
    void                              DeletePointerFileEntries(Func<PointerFileEntry, bool> shouldBeDeleted);
}