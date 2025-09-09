using Arius.Core.Shared.Hashing;
using Zio;

namespace Arius.Core.Shared.StateRepositories;

internal interface IStateRepository
{
    FileEntry                     StateDatabaseFile { get; }
    bool                          HasChanges        { get; }
    void                          Vacuum();
    void                          Delete();
    BinaryProperties?             GetBinaryProperty(Hash h);
    void                          AddBinaryProperties(params BinaryProperties[] bps);
    void                          UpsertPointerFileEntries(params PointerFileEntry[] pfes);
    IEnumerable<PointerFileEntry> GetPointerFileEntries(string relativeNamePrefix, bool includeBinaryProperties = false);
    void                          DeletePointerFileEntries(Func<PointerFileEntry, bool> shouldBeDeleted);
}