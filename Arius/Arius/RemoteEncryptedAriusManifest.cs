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
        /// Create a New Manifest & upload it
        /// </summary>
        public static RemoteEncryptedAriusManifest Create(string localContentFileHash, IEnumerable<RemoteEncryptedAriusChunk> chunks, AriusRemoteArchive archive, string passphrase)
        {
            //if (lcfs.Select(lcf => lcf.Hash).Distinct().Count() > 1)
            //    throw new ArgumentException(
            //        "The specified LocalContentFiles have different hashes and do not belong to the same manifest");

            //TODO manifest does not yet exit remte
            //if (archive.GetRemoteEncryptedAriusManifestFileByHash(lcfs.First().Hash)

            var manifest = new AriusManifest(chunks.Select(c => c.Name), localContentFileHash);
            var remoteManifest = manifest.Create(archive, passphrase);

            return remoteManifest;
        }

        
        public RemoteEncryptedAriusManifest(AriusRemoteArchive archive, BlobItem bi) : base(archive, bi)
        {
            if (!bi.Name.EndsWith(".manifest.7z.arius"))
                throw new ArgumentException("NOT A MANIFEST"); //TODO
        }

        public override string Hash => _bi.Name.TrimEnd(".manifest.7z.arius");


        /// <summary>
        /// Synchronize the remote manifest with the current local file system entries
        /// </summary>
        /// <param name="lcfs">The current (as per the file system) LocalContentFiles for this manifest</param>
        /// <param name="passphrase"></param>
        public void Synchronize(IEnumerable<AriusPointerFile> lcfs, string passphrase)
        {
            var manifest = AriusManifest.FromRemote(this, passphrase);
            manifest.Synchronize(lcfs, _archive, passphrase);
        }


        internal class AriusManifest //Marked as internal for Unit Testing
        {
            // --- CONSTRUCTORS
            public static AriusManifest FromRemote(RemoteEncryptedAriusManifest ream, string passphrase)
            {
                //Download the existing EncryptedManifest
                var tempEncryptedAriusManifestFileFullname = Path.GetTempFileName();
                ream._archive.Download(ream.Name, tempEncryptedAriusManifestFileFullname);

                //Get the decrypted manifest
                var szu = new SevenZipUtils();
                var tempDecryptedAriusManifestFileFullName = Path.GetTempFileName();
                szu.DecryptFile(tempEncryptedAriusManifestFileFullname, tempDecryptedAriusManifestFileFullName, passphrase);
                File.Delete(tempEncryptedAriusManifestFileFullname);

                //Get the Manifest
                var json = File.ReadAllText(tempDecryptedAriusManifestFileFullName);
                File.Delete(tempDecryptedAriusManifestFileFullName);
                var manifest = AriusManifest.FromJson(json);

                return manifest;
            }

            private string AsJson() =>
                JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true, IgnoreNullValues = true });

            private static AriusManifest FromJson(string json) => JsonSerializer.Deserialize<AriusManifest>(json);

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
            internal IEnumerable<AriusPointerFileEntry> GetLastEntriesPerRelativeName()
            {
                return _ariusPointerFileEntries
                    .GroupBy(lcfe => lcfe.RelativeName)
                    .Select(g => g
                        .OrderBy(lcfe => lcfe.Version)
                        .Last());
            }
            /// <summary>
            /// Synchronize the state of the manifest to the current state of the file system:
            /// Additions, deletions, renames (= add + delete)
            /// </summary>
            public void Synchronize(IEnumerable<AriusPointerFile> apfs, AriusRemoteArchive archive, string passphrase)
            {
                var fileSystemEntries = GetAriusManifestEntries(apfs);
                var lastEntries = GetLastEntriesPerRelativeName().ToImmutableArray();

                var ameec = new AriusManifestEntryEqualityComparer();

                var addedFiles = fileSystemEntries.Except(lastEntries, ameec).ToList();
                var deletedFiles = lastEntries
                    .Except(fileSystemEntries, ameec)
                    .Select(lcfe => lcfe with { IsDeleted = true, CreationTimeUtc = null, LastWriteTimeUtc = null})
                    .ToList();

                _ariusPointerFileEntries.AddRange(addedFiles);
                _ariusPointerFileEntries.AddRange(deletedFiles);

                if (addedFiles.Any() || deletedFiles.Any())
                    Update(archive, passphrase);
            }

            public RemoteEncryptedAriusManifest Create(AriusRemoteArchive archive, string passphrase)
            {
                Update(archive, passphrase);

                return archive.GetRemoteEncryptedAriusManifestByHash(Hash);
            }

            public void Update(AriusRemoteArchive archive, string passphrase)
            {
                var tempAriusManifestName = Path.GetTempFileName();
                var json = AsJson();
                File.WriteAllText(tempAriusManifestName, json);

                var szu = new SevenZipUtils();
                var tempAriusEncryptedManifestName = Path.GetTempFileName();
                szu.EncryptFile(tempAriusManifestName, tempAriusEncryptedManifestName, passphrase, CompressionLevel.Normal);
                File.Delete(tempAriusManifestName);

                // Upload it
                archive.UploadEncryptedAriusManifest(tempAriusEncryptedManifestName, Hash);
                File.Delete(tempAriusEncryptedManifestName);
            }


            // --- RECORD DEFINITION & HELPERS
            private static List<AriusPointerFileEntry> GetAriusManifestEntries(IEnumerable<AriusPointerFile> localContentFiles)
            {
                return localContentFiles.Select(lcf => GetAriusManifestEntry(lcf)).ToList();
            }
            private static AriusPointerFileEntry GetAriusManifestEntry(AriusPointerFile lcf)
            {
                return new AriusPointerFileEntry(lcf.RelativeLocalContentFileName, DateTime.UtcNow, false, lcf.CreationTimeUtc, lcf.LastWriteTimeUtc);
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
    }
}