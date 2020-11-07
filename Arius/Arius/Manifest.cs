using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Arius
{
    class AriusManifest
    {
        public static AriusManifest CreateManifest(LocalContentFile lcf, params EncryptedAriusChunk[] chunks)
        {
            var me = new AriusManifestEntry
            {
                RelativeName = lcf.RelativeName,
                Version = DateTime.UtcNow,
                IsDeleted = false,
                EncryptedChunks = chunks.Select(c => c.Name),
                CreationTimeUtc = lcf.CreationTimeUtc,
                LastWriteTimeUtc = lcf.LastWriteTimeUtc,
                Hash = lcf.Hash
            };

            return new AriusManifest
            {
                Entries = new List<AriusManifestEntry>(new AriusManifestEntry[] { me })
            };
        }

        public List<AriusManifestEntry> Entries;

        public AriusManifestFile GetAriusManifestFile(string ariusManifestFullName) => AriusManifestFile.GetAriusManifestFile(ariusManifestFullName, this);

        public struct AriusManifestEntry
        {
            public string RelativeName { get; set; }
            public DateTime Version { get; set; }
            public bool IsDeleted { get; set; }
            public IEnumerable<string> EncryptedChunks { get; set; }
            public DateTime CreationTimeUtc { get; set; }
            public DateTime LastWriteTimeUtc { get; set; }
            public string Hash { get; set; }
        }

        public string AsJson() => JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true }); // TODO waarom niet gewoon Serialize(this)
        public AriusManifest FromJson(string json) => JsonSerializer.Deserialize<AriusManifest>(json);
    }

    /// <summary>
    /// De Pointer
    /// </summary>
    class AriusManifestFile : AriusFile
    {
        //public static AriusManifestFile GetAriusManifest(LocalContentFile lcf, params EncryptedAriusChunk[] chunks)
        //{
        //    var m = AriusManifest.CreateManifest(lcf, chunks);
        //    var ariusManifestFullName = GetAriusManifestFullName(lcf);

        //    File.WriteAllText(ariusManifestFullName, m.AsJson);

        //    var fi = new FileInfo(ariusManifestFullName);
        //    return new AriusManifestFile(fi);
        //}
        public static AriusManifestFile GetAriusManifestFile(string ariusManifestFullName, AriusManifest ariusManifest)
        {
            var json = ariusManifest.AsJson();
            File.WriteAllText(ariusManifestFullName, json);

            var fi = new FileInfo(ariusManifestFullName);
            return new AriusManifestFile(fi);
        }

        private AriusManifestFile(FileInfo ariusManifestFile) : base(ariusManifestFile)
        {
        }

        public EncryptedAriusManifestFile AsEncryptedAriusManifestFile(string passphrase)
        {
            return EncryptedAriusManifestFile.GetEncryptedAriusManifestFile(this, passphrase);
        }
    }

    class EncryptedAriusManifestFile : AriusFile
    {
        public EncryptedAriusManifestFile(FileInfo file) : base(file) { }

        public static EncryptedAriusManifestFile GetEncryptedAriusManifestFile(AriusManifestFile ariusManifestFile, string passphrase)
        {
            //var encryptedAriusChunkFullName = GetEncryptedAriusManifestFileFullName(ariusChunk);

            //var szu = new SevenZipUtils();
            //szu.EncryptFile(ariusChunk.FullName, encryptedAriusChunkFullName, passphrase);

            //return new EncryptedAriusChunk(new FileInfo(encryptedAriusChunkFullName));
        }

        private static string GetEncryptedAriusManifestFileFullName(AriusChunk chunk) => $"{Path.Combine(chunk.DirectoryName, chunk.Hash)}.7z.arius";
    }

    
}
