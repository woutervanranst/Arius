using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs.Models;
using SevenZip;

namespace Arius
{
    internal class AriusManifest
    {
        public static AriusManifest Create(LocalContentFile lcf, params EncryptedAriusChunk[] chunks)
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
                Entries = new List<AriusManifestEntry>(new[] { me })
            };
        }

        public List<AriusManifestEntry> Entries;

        public AriusManifestFile CreateAriusManifestFile(string ariusManifestFullName)
        {
            return AriusManifestFile.Create(ariusManifestFullName, this);
        }

        public string AsJson() => JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true }); // TODO waarom niet gewoon Serialize(this)
        public AriusManifest FromJson(string json) => JsonSerializer.Deserialize<AriusManifest>(json);


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
    }

    /// <summary>
    /// De Manifest
    /// </summary>
    internal class AriusManifestFile : AriusFile
    {
        public static AriusManifestFile Create(string ariusManifestFullName, AriusManifest ariusManifest)
        {
            var json = ariusManifest.AsJson();
            File.WriteAllText(ariusManifestFullName, json);

            var fi = new FileInfo(ariusManifestFullName);
            return new AriusManifestFile(fi);
        }

        private AriusManifestFile(FileInfo ariusManifestFile) : base(ariusManifestFile)
        {
        }

        public EncryptedAriusManifestFile CreateEncryptedAriusManifestFile(string encryptedAriusManifestFileFullName, string passphrase, bool deleteUnencryptedManifestFile)
        {
            var eamf = EncryptedAriusManifestFile.Create(encryptedAriusManifestFileFullName, this, passphrase);
            if (deleteUnencryptedManifestFile)
                base.Delete();

            return eamf;
        }
    }

    internal class EncryptedAriusManifestFile : AriusFile
    {
        public EncryptedAriusManifestFile(FileInfo file) : base(file) { }

        public static EncryptedAriusManifestFile Create(string encryptedAriusManifestFileFullName, AriusManifestFile ariusManifestFile, string passphrase)
        {
            //var encryptedAriusManifestFileFullName = EncryptedAriusManifestFileFullName(ariusManifestFile);

            var szu = new SevenZipUtils();
            szu.EncryptFile(ariusManifestFile.FullName, encryptedAriusManifestFileFullName, passphrase, CompressionLevel.Normal);

            return new EncryptedAriusManifestFile(new FileInfo(encryptedAriusManifestFileFullName));
        }

        //private static string EncryptedAriusManifestFileFullName(AriusManifestFile ariusManifestFile) => $"{ariusManifestFile.FullName}.7z.arius";

        internal AriusPointerFile CreatePointerFile(LocalContentFile lcf)
        {
            return AriusPointerFile.Create(lcf.AriusPointerFileFullName, Name);
        }

        public override string ToString() => base.Name;
    }

    internal class RemoteEncryptedAriusManifestFile
    {
        public static RemoteEncryptedAriusManifestFile Create(BlobItem bi)
        {
            return new RemoteEncryptedAriusManifestFile(bi);
        }

        private RemoteEncryptedAriusManifestFile(BlobItem bi)
        {
            _bi = bi;
        }

        private readonly BlobItem _bi;

        /// <summary>
        /// Hash of the unencrypted content
        /// </summary>
        public string Hash => _bi.Name.Substring(0, _bi.Name.Length - ".manifest.7z.arius".Length);
    }
}