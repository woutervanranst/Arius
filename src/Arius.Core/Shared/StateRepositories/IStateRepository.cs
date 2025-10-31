using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Zio;

namespace Arius.Core.Shared.StateRepositories;

internal interface IStateRepository
{
    /// <summary>
    /// Gets the state repository database file that backs this repository instance.
    /// </summary>
    FileEntry StateDatabaseFile { get; }

    /// <summary>
    /// Gets a value indicating whether the repository has tracked changes that are not yet persisted.
    /// </summary>
    bool HasChanges { get; }

    /// <summary>
    /// Compacts the underlying state database to reclaim unused space.
    /// </summary>
    void Vacuum();

    /// <summary>
    /// Deletes the underlying state database and resets the repository.
    /// </summary>
    void Delete();

    /// <summary>
    /// Retrieves the binary properties for the specified hash, or <c>null</c> when absent.
    /// </summary>
    /// <param name="h">The hash that identifies the binary.</param>
    BinaryProperties? GetBinaryProperty(Hash h);

    /// <summary>
    /// Updates the storage tier for the binary that matches the given hash.
    /// </summary>
    /// <param name="h">The hash that identifies the binary.</param>
    /// <param name="tier">The new storage tier to apply.</param>
    void SetBinaryPropertyArchiveTier(Hash h, StorageTier tier);

    /// <summary>
    /// Adds the provided binary property records to the repository.
    /// </summary>
    /// <param name="bps">The binary property entries to persist.</param>
    void AddBinaryProperties(params BinaryProperties[] bps);

    /// <summary>
    /// Inserts new pointer file entries or updates existing ones as needed.
    /// </summary>
    /// <param name="pfes">The pointer file entries to upsert.</param>
    void UpsertPointerFileEntries(params PointerFileEntry[] pfes);

    /// <summary>
    /// Enumerates pointer file directories that match the specified prefix.
    /// </summary>
    /// <param name="relativeNamePrefix">A prefix that must start with '/' and is matched against stored entries.</param>
    /// <param name="topDirectoryOnly">When <c>true</c>, returns only directories one level below the prefix.</param>
    IEnumerable<PointerFileDirectory> GetPointerFileDirectories(string relativeNamePrefix, bool topDirectoryOnly);

    /// <summary>
    /// Enumerates pointer file entries that match the specified prefix.
    /// </summary>
    /// <param name="relativeNamePrefix">A prefix that must start with '/' and is matched against stored entries.</param>
    /// <param name="topDirectoryOnly">When <c>true</c>, limits results to the first level of the hierarchy.</param>
    /// <param name="includeBinaryProperties">When <c>true</c>, includes related binary properties in the results.</param>
    IEnumerable<PointerFileEntry> GetPointerFileEntries(string relativeNamePrefix, bool topDirectoryOnly, bool includeBinaryProperties = false);

    /// <summary>
    /// Retrieves a single pointer file entry that matches the supplied relative name, or <c>null</c> if none exists.
    /// </summary>
    /// <param name="relativeName">The full relative name, starting with '/'.</param>
    /// <param name="includeBinaryProperties">When <c>true</c>, includes the related binary properties.</param>
    PointerFileEntry? GetPointerFileEntry(string relativeName, bool includeBinaryProperties = false);

    /// <summary>
    /// Deletes pointer file entries that satisfy the supplied predicate.
    /// </summary>
    /// <param name="shouldBeDeleted">A predicate used to determine which entries to remove.</param>
    /// <returns>The number of deleted entries.</returns>
    int DeletePointerFileEntries(Func<PointerFileEntry, bool> shouldBeDeleted);
}
