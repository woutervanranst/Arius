using Arius.Core.Models;

namespace Arius.Core.Repositories;

internal interface IStateRepository
{
    FileInfo                         StateDatabaseFile { get; }
    bool                             HasChanges        { get; }
    void                             Vacuum();
    void                             Delete();
    BinaryPropertiesDto?             GetBinaryProperty(Hash h);
    void                             AddBinaryProperties(params BinaryPropertiesDto[] bps);
    void                             UpsertPointerFileEntries(params PointerFileEntryDto[] pfes);
    IEnumerable<PointerFileEntryDto> GetPointerFileEntries(string relativeNamePrefix, bool includeBinaryProperties = false);
    void                             DeletePointerFileEntries(Func<PointerFileEntryDto, bool> shouldBeDeleted);
}