using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arius;

internal class AriusManifest //Marked as internal for Unit Testing
{
    [JsonConstructor]
    public AriusManifest(IEnumerable<AriusPointerFileEntry> ariusPointerFileEntries, IEnumerable<string> encryptedChunks, string hash)
    {
        _ariusPointerFileEntries = ariusPointerFileEntries.ToList();
        EncryptedChunks = encryptedChunks;
        Hash = hash;
    }
    public AriusManifest(IEnumerable<string> encryptedChunks, string hash)
    {
        _ariusPointerFileEntries = new List<AriusPointerFileEntry>();
        EncryptedChunks = encryptedChunks;
        Hash = hash;
    }

    // --- PROPERTIES

    [JsonInclude]
    public IEnumerable<AriusPointerFileEntry> AriusPointerFileEntries => _ariusPointerFileEntries;
    private readonly List<AriusPointerFileEntry> _ariusPointerFileEntries;

    [JsonInclude]
    public IEnumerable<string> EncryptedChunks { get; private set; }

    /// <summary>
    /// Hash of the unencrypted LocalContentFiles
    /// </summary>
    [JsonInclude]
    public string Hash { get; private set; }

    // --- METHODS
    internal IEnumerable<AriusPointerFileEntry> GetLastExistingEntriesPerRelativeName(bool includeLastDeleted = false)
    {
        var r = _ariusPointerFileEntries
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
    public bool Update(IEnumerable<IKaka> apfs)
    {
        var fileSystemEntries = GetAriusManifestEntries(apfs);
        var lastEntries = GetLastExistingEntriesPerRelativeName().ToImmutableArray();

        var ameec = new AriusManifestEntryEqualityComparer();

        var addedFiles = fileSystemEntries.Except(lastEntries, ameec).ToList();
        var deletedFiles = lastEntries
            .Except(fileSystemEntries, ameec)
            .Select(lcfe => lcfe with { IsDeleted = true, CreationTimeUtc = null, LastWriteTimeUtc = null })
            .ToList();

        _ariusPointerFileEntries.AddRange(addedFiles);
        _ariusPointerFileEntries.AddRange(deletedFiles);

        return addedFiles.Any() || deletedFiles.Any();
    }

    //public RemoteEncryptedAriusManifest Create(AriusRemoteArchive archive, string passphrase)
    //{
    //    Update(archive, passphrase);

    //    return archive.GetRemoteEncryptedAriusManifestByHash(Hash);
    //}

    // --- RECORD DEFINITION & HELPERS
    private static List<AriusPointerFileEntry> GetAriusManifestEntries(IEnumerable<IKaka> localContentFiles)
    {
        return localContentFiles.Select(lcf => GetAriusManifestEntry(lcf)).ToList();
    }
    private static AriusPointerFileEntry GetAriusManifestEntry(IKaka lcf)
    {
        return new AriusPointerFileEntry(lcf.RelativeContentName, 
            DateTime.UtcNow, 
            false, 
            lcf.CreationTimeUtc,
            lcf.LastWriteTimeUtc);
    }


    public sealed record AriusPointerFileEntry(string RelativeName, DateTime Version, bool IsDeleted, DateTime? CreationTimeUtc, DateTime? LastWriteTimeUtc);


    private class AriusManifestEntryEqualityComparer : IEqualityComparer<AriusPointerFileEntry>
    {

        public bool Equals(AriusPointerFileEntry x, AriusPointerFileEntry y)
        {
            return x.RelativeName == y.RelativeName &&
                   //x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version
                   x.IsDeleted == y.IsDeleted &&
                   x.CreationTimeUtc.Equals(y.CreationTimeUtc) &&
                   x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc);
        }

        public int GetHashCode(AriusPointerFileEntry obj)
        {
            return HashCode.Combine(obj.RelativeName,
                //obj.Version,  //DO NOT Compare on DateTime Version
                obj.IsDeleted,
                obj.CreationTimeUtc,
                obj.LastWriteTimeUtc);
        }
    }
}