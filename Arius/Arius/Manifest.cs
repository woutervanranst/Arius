using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arius;

internal class Manifest //Marked as internal for Unit Testing
{
    [JsonConstructor]
    public Manifest(IEnumerable<PointerFileEntry> pointerFileEntries, IEnumerable<string> encryptedChunks, string hash)
    {
        _PointerFileEntries = pointerFileEntries.ToList();
        EncryptedChunks = encryptedChunks;
        Hash = hash;
    }
    public Manifest(IEnumerable<ILocalContentFile> localContentFiles, IEnumerable<string> encryptedChunks, string hash)
    {
        _PointerFileEntries = GetPointerFileEntries(localContentFiles); // new List<AriusPointerFileEntry>();
        EncryptedChunks = encryptedChunks;
        Hash = hash;
    }

    private static readonly PointerFileEntryEqualityComparer _pfeec = new PointerFileEntryEqualityComparer();

    // --- PROPERTIES

    [JsonInclude]
    public IEnumerable<PointerFileEntry> PointerFileEntries => _PointerFileEntries;
    private readonly List<PointerFileEntry> _PointerFileEntries;

    [JsonInclude]
    public IEnumerable<string> EncryptedChunks { get; private set; }

    /// <summary>
    /// Hash of the unencrypted LocalContentFiles
    /// </summary>
    [JsonInclude]
    public string Hash { get; private set; }

    // --- METHODS
    internal IEnumerable<PointerFileEntry> GetLastExistingEntriesPerRelativeName(bool includeLastDeleted = false)
    {
        var r = _PointerFileEntries
            .GroupBy(lcfe => lcfe.RelativeName)
            .Select(g => g
                .OrderBy(lcfe => lcfe.Version)
                .Last());

        if (includeLastDeleted)
            return r;
        else
            return r.Where(afpe => !afpe.IsDeleted);
    }
    /// <summary>
    /// Synchronize the state of the manifest to the current state of the file system:
    /// Additions, deletions, renames (= add + delete)
    /// </summary>
    public bool Update(IEnumerable<IArchivable> apfs)
    {
        var fileSystemEntries = GetPointerFileEntries(apfs);
        var lastEntries = GetLastExistingEntriesPerRelativeName().ToImmutableArray();

        var addedFiles = fileSystemEntries.Except(lastEntries, _pfeec).ToList();
        var deletedFiles = lastEntries
            .Except(fileSystemEntries, _pfeec)
            .Select(lcfe => lcfe with { IsDeleted = true, CreationTimeUtc = null, LastWriteTimeUtc = null })
            .ToList();

        _PointerFileEntries.AddRange(addedFiles);
        _PointerFileEntries.AddRange(deletedFiles);

        return addedFiles.Any() || deletedFiles.Any();
    }

    //public RemoteEncryptedAriusManifest Create(AriusRemoteArchive archive, string passphrase)
    //{
    //    Update(archive, passphrase);

    //    return archive.GetRemoteEncryptedAriusManifestByHash(Hash);
    //}

    // --- RECORD DEFINITION & HELPERS
    private static List<PointerFileEntry> GetPointerFileEntries(IEnumerable<IArchivable> localContentFiles)
    {
        return localContentFiles.Select(lcf => GetPointerFileEntry(lcf)).ToList();
    }
    private static PointerFileEntry GetPointerFileEntry(IArchivable lcf)
    {
        return new PointerFileEntry(lcf.RelativeContentName, 
            DateTime.UtcNow, 
            false, 
            lcf.CreationTimeUtc,
            lcf.LastWriteTimeUtc);
    }


    public sealed record PointerFileEntry(string RelativeName, DateTime Version, bool IsDeleted, DateTime? CreationTimeUtc, DateTime? LastWriteTimeUtc);


    private class PointerFileEntryEqualityComparer : IEqualityComparer<PointerFileEntry>
    {

        public bool Equals(PointerFileEntry x, PointerFileEntry y)
        {
            return x.RelativeName == y.RelativeName &&
                   //x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version
                   x.IsDeleted == y.IsDeleted &&
                   x.CreationTimeUtc.Equals(y.CreationTimeUtc) &&
                   x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc);
        }

        public int GetHashCode(PointerFileEntry obj)
        {
            return HashCode.Combine(obj.RelativeName,
                //obj.Version,  //DO NOT Compare on DateTime Version
                obj.IsDeleted,
                obj.CreationTimeUtc,
                obj.LastWriteTimeUtc);
        }
    }
}