using Azure.Storage.Blobs;
using SevenZip;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    internal class RemoteEncryptedAriusManifest : RemoteAriusFile
    {
        /// <summary>
        /// Create a New Manifest for the given LocalContentFiles & upload it
        /// The LocalContentFiles all have the same hash (ie are 'copies')
        /// </summary>
        /// <param name="lcfs"></param>
        /// <param name="chunks"></param>
        /// <returns></returns>
        public static RemoteEncryptedAriusManifest Create(IEnumerable<LocalContentFile> lcfs, IEnumerable<RemoteEncryptedAriusChunk> chunks, AriusRemoteArchive archive, string passphrase)
        {
            if (lcfs.Select(lcf => lcf.Hash).Distinct().Count() > 1)
                throw new ArgumentException(
                    "The specified LocalContentFiles have different hashes and do not belong to the same manifest");

            //TODO manifest does not yet exit remte
            //if (archive.GetRemoteEncryptedAriusManifestFileByHash(lcfs.First().Hash)

            var manifest = new AriusManifest(
                lcfs.Select(lcf => AriusManifest.GetAriusManifestEntry(lcf)).ToList(),
                chunks.Select(c => c.Name),
                lcfs.First().Hash);


            // Save it
            var tempAriusManifestName = Path.GetTempFileName();
            var json = manifest.AsJson();
            File.WriteAllText(tempAriusManifestName, json);

            var szu = new SevenZipUtils();
            var tempAriusEncryptedManifestName = Path.GetTempFileName(); // Path.Combine(Path.GetTempPath(), manifest.EncryptedAriusManifestName);
            szu.EncryptFile(tempAriusManifestName, tempAriusEncryptedManifestName, passphrase, CompressionLevel.Normal);
            File.Delete(tempAriusManifestName);

            // Upload it
            var remoteManifest = archive.UploadEncryptedAriusManifest(tempAriusEncryptedManifestName, manifest.Hash);
            File.Delete(tempAriusEncryptedManifestName);

            return remoteManifest;
        }

        public static RemoteEncryptedAriusManifest FromRemote(AriusRemoteArchive archive, string hash, string passphrase)
        {
            throw new NotImplementedException();

            //var blobName = GetEncryptedAriusManifestBlobName(hash);

            ////Download the existing EncryptedManifest
            //var tempEncryptedAriusManifestFileFullname = Path.GetTempFileName();
            //archive.Download(blobName, tempEncryptedAriusManifestFileFullname);

            ////Get the decrypted manifest
            //var szu = new SevenZipUtils();
            //var tempDecryptedAriusManifestFileFullName = Path.GetTempFileName();
            //szu.DecryptFile(tempEncryptedAriusManifestFileFullname, tempDecryptedAriusManifestFileFullName, passphrase);
            //File.Delete(tempEncryptedAriusManifestFileFullname);

            ////Get the Manifest
            //var json = File.ReadAllText(tempDecryptedAriusManifestFileFullName);
            //File.Delete(tempDecryptedAriusManifestFileFullName);
            //var manifest = AriusManifest.FromJson(json);

            //return manifest;
        }

        private static string GetEncryptedAriusManifestBlobName(string hash) => $"{hash}.manifest.7z.arius";


        public RemoteEncryptedAriusManifest(BlobItem bi) : base(bi)
        {
            if (!bi.Name.EndsWith(".manifest.7z.arius"))
                throw new ArgumentException("NOT A MANIFEST"); //TODO
        }

        public override string Hash => _bi.Name.TrimEnd(".manifest.7z.arius");



        private class AriusManifest
        {
            // --- CONSTRUCTORS
            public string AsJson() =>
                JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true });

            public static AriusManifest FromJson(string json) => JsonSerializer.Deserialize<AriusManifest>(json);

            [JsonConstructor]
            public AriusManifest(IEnumerable<LocalContentFileEntry> localContentFileEntries, IEnumerable<string> encryptedChunks, string hash)
            {
                _localContentFiles = localContentFileEntries.ToList();
                EncryptedChunks = encryptedChunks;
                Hash = hash;
            }

            // --- PROPERTIES

            [JsonInclude]
            public IEnumerable<LocalContentFileEntry> LocalContentFiles => _localContentFiles;
            private readonly List<LocalContentFileEntry> _localContentFiles;
            [JsonInclude]
            public IEnumerable<string> EncryptedChunks { get; private set; }

            /// <summary>
            /// Hash of the unencrypted LocalContentFiles
            /// </summary>
            [JsonInclude]
            public string Hash { get; private set; }

            // --- METHODS
            public void AddEntry(LocalContentFile lcf)
            {
                var me = GetAriusManifestEntry(lcf);

                if (!_localContentFiles.Contains(me, new AriusManifestEntryEqualityComparer()))
                    _localContentFiles.Add(me);
            }


            // --- STATIC HELPER METHODS
            public static LocalContentFileEntry GetAriusManifestEntry(LocalContentFile lcf)
            {
                return new LocalContentFileEntry(lcf.RelativeName, DateTime.UtcNow, false, lcf.CreationTimeUtc, lcf.LastWriteTimeUtc);
            }

            public sealed record LocalContentFileEntry(string RelativeName, DateTime Version, bool IsDeleted, DateTime CreationTimeUtc, DateTime LastWriteTimeUtc);


            internal class AriusManifestEntryEqualityComparer : IEqualityComparer<LocalContentFileEntry>
            {
                public bool Equals(LocalContentFileEntry x, LocalContentFileEntry y)
                {
                    return x.RelativeName == y.RelativeName &&
                           //x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version
                           x.IsDeleted == y.IsDeleted &&
                           x.CreationTimeUtc.Equals(y.CreationTimeUtc) &&
                           x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc);
                }

                public int GetHashCode(LocalContentFileEntry obj)
                {
                    return HashCode.Combine(obj.RelativeName,
                        //obj.Version,  //DO NOT Compare on DateTime Version
                        obj.IsDeleted,
                        obj.CreationTimeUtc,
                        obj.LastWriteTimeUtc);
                }
            }
        }
    }
}