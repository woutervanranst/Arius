namespace Arius
{
    //class Manifest
    //{
    //    public static Manifest CreateManifest(string contentBlobName, string relativeFileName)
    //    {
    //        var manifest = new Manifest(contentBlobName, new List<ManifestEntry>());
    //        manifest.AddEntry(relativeFileName);

    //        return manifest;
    //    }

    //    public static Manifest GetManifest(AriusRemoteArchive blobUtils, SevenZipUtils sevenZipUtils, string contentBlobName, string passphrase)
    //    {
    //        var m = new Manifest(contentBlobName);

    //        var tempFileName1 = Path.GetTempFileName();
    //        blobUtils.Download(m.EncryptedManifestBlobName, tempFileName1);

    //        var tempFileName2 = Path.GetTempFileName();
    //        sevenZipUtils.DecryptFile(tempFileName1, tempFileName2, passphrase);
    //        File.Delete(tempFileName1);

    //        var json = File.ReadAllText(tempFileName2);
    //        File.Delete(tempFileName2);

    //        m._entries = JsonSerializer.Deserialize<List<ManifestEntry>>(json);

    //        return m;
    //    }

    //    private List<ManifestEntry> _entries;


    //    private Manifest(string contentBlobName, List<ManifestEntry> entries = null)
    //    {
    //        ContentBlobName = contentBlobName;
    //        _entries = entries ?? new List<ManifestEntry>();
    //    }

    //    /// <summary>
    //    /// Get all ManifestEntries
    //    /// </summary>
    //    /// <param name="includeDeleted"></param>
    //    /// <returns></returns>
    //    public IEnumerable<ManifestEntry> GetAllEntries(bool includeDeleted)
    //    {
    //        if (_entries.Count == 0)
    //            throw new ArgumentException("Entries not initialized");

    //        if (includeDeleted)
    //            return _entries.AsEnumerable();
    //        else
    //            return _entries.Where(me => !me.IsDeleted).AsEnumerable();
    //    }

    //    /// <summary>
    //    /// Get all ManifestEntries for the given relative path/filename
    //    /// </summary>
    //    /// <param name="includeDeleted"></param>
    //    /// <param name="relativeFileName"></param>
    //    /// <returns></returns>
    //    public IEnumerable<ManifestEntry> GetAllEntries(bool includeDeleted, string relativeFileName)
    //    {
    //        return GetAllEntries(includeDeleted)
    //            .Where(me => me.RelativeFileName == relativeFileName)
    //            .AsEnumerable();
    //    }

    //    /// <summary>
    //    /// Get the last (per file and ordered by date) ManifestEntries
    //    /// </summary>
    //    /// <param name="includeDeleted"></param>
    //    /// <returns></returns>
    //    public IEnumerable<ManifestEntry> GetLastEntries(bool includeDeleted)
    //    {
    //        var latestEntries = GetAllEntries(true)  //NOTE: niet GetAllEntries(includeDeleted) want dan hebt ge de entires die zeggen datm deleted is niet
    //            .GroupBy(me => me.RelativeFileName, me => me)
    //            .Select(g => g.OrderBy(me => me.DateTime).Last());

    //        if (includeDeleted)
    //            return latestEntries
    //                .AsEnumerable();
    //        else
    //            return latestEntries
    //                .Where(m => !m.IsDeleted)
    //                .AsEnumerable();
    //    }

    //    public IEnumerable<LocalAriusFile> GetLocalAriusFiles(DirectoryInfo root)
    //    {
    //        var les = GetLastEntries(false);

    //        return les.Select(me => new LocalAriusFile(root, me.RelativeLocalAriusFileName, this));
    //    }

    //    public string ContentBlobName { get; private set; }
    //    public string ManifestBlobName => $"{ContentBlobName}.manifest";
    //    public string EncryptedManifestBlobName => $"{ContentBlobName}.manifest.7z.arius";

    //    public void AddEntry(string relativeFileName, bool isDeleted = false)
    //    {
    //        _entries.Add(new ManifestEntry { RelativeFileName = relativeFileName, DateTime = DateTime.UtcNow, IsDeleted = isDeleted });
    //    }

    //    public void Archive(AriusRemoteArchive blobUtils, SevenZipUtils sevenZipUtils, string passphrase)
    //    {
    //        var tempManifestFileName = Path.Combine(Path.GetTempPath(), ManifestBlobName);
    //        var json = JsonSerializer.Serialize(_entries);
    //        File.WriteAllText(tempManifestFileName, json);

    //        var tempEncryptedManifestFileName = Path.GetTempFileName();

    //        sevenZipUtils.EncryptFile(tempManifestFileName, tempEncryptedManifestFileName, passphrase);
    //        File.Delete(tempManifestFileName);

    //        blobUtils.Archive(tempEncryptedManifestFileName, EncryptedManifestBlobName, AccessTier.Cool);
    //        File.Delete(tempEncryptedManifestFileName);
    //    }


    //}

    //class ManifestEntry
    //{
    //    public string RelativeFileName { get; set; }

    //    [JsonIgnore]
    //    public string RelativeLocalAriusFileName => $"{RelativeFileName}.arius";

    //    public DateTime DateTime { get; set; }

    //    public bool IsDeleted { get; set; }
    //}
}
