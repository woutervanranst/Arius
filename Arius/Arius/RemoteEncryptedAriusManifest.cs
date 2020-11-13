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

            var manifest = new AriusManifest(lcfs, chunks.Select(c => c.Name), lcfs.First().Hash);
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
        public void Synchronize(IEnumerable<ILocalFile> lcfs, string passphrase)
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
            public AriusManifest(IEnumerable<LocalContentFileEntry> localContentFileEntries, IEnumerable<string> encryptedChunks, string hash)
            {
                _localContentFileEntries = localContentFileEntries.ToList();
                EncryptedChunks = encryptedChunks;
                Hash = hash;
            }
            public AriusManifest(IEnumerable<LocalContentFile> localContentFiles, IEnumerable<string> encryptedChunks, string hash)
            {
                _localContentFileEntries = GetAriusManifestEntries(localContentFiles);
                EncryptedChunks = encryptedChunks;
                Hash = hash;
            }

            // --- PROPERTIES

            [JsonInclude]
            public IEnumerable<LocalContentFileEntry> LocalContentFileEntries => _localContentFileEntries;
            private readonly List<LocalContentFileEntry> _localContentFileEntries;
            
            [JsonInclude]
            public IEnumerable<string> EncryptedChunks { get; private set; }

            /// <summary>
            /// Hash of the unencrypted LocalContentFiles
            /// </summary>
            [JsonInclude]
            public string Hash { get; private set; }

            // --- METHODS
            internal IEnumerable<LocalContentFileEntry> GetLastEntriesPerRelativeName()
            {
                return _localContentFileEntries
                    .GroupBy(lcfe => lcfe.RelativeName)
                    .Select(g => g
                        .OrderBy(lcfe => lcfe.Version)
                        .Last());
            }
            /// <summary>
            /// Synchronize the state of the manifest to the current state of the file system:
            /// Additions, deletions, renames (= add + delete)
            /// </summary>
            public void Synchronize<T>(IEnumerable<T> lcfs, AriusRemoteArchive archive, string passphrase) where T : ILocalFile
            {
                var fileSystemEntries = GetAriusManifestEntries((IEnumerable<ILocalFile>)lcfs);
                var lastEntries = GetLastEntriesPerRelativeName().ToImmutableArray();

                var genericTIsAriusPointerFile = (lcfs.First() as AriusPointerFile) is not null; //When Comparing with AriusPointerFiles, we ignore Created/lastWrite time
                        //TODO TEST VOOR LocalContentFIle
                var ameec = new AriusManifestEntryEqualityComparer(genericTIsAriusPointerFile); // https://stackoverflow.com/questions/39244449/cast-generic-type-parameter-to-a-specific-type-in-c-sharp/39244683

                var addedFiles = fileSystemEntries.Except(lastEntries, ameec).ToList();
                var deletedFiles = lastEntries
                    .Except(fileSystemEntries, ameec)
                    .Select(lcfe => lcfe with { IsDeleted = true, CreationTimeUtc = null, LastWriteTimeUtc = null})
                    .ToList();

                _localContentFileEntries.AddRange(addedFiles);
                _localContentFileEntries.AddRange(deletedFiles);

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
            private static List<LocalContentFileEntry> GetAriusManifestEntries(IEnumerable<ILocalFile> localContentFiles)
            {
                return localContentFiles.Select(lcf => GetAriusManifestEntry(lcf)).ToList();
            }
            private static LocalContentFileEntry GetAriusManifestEntry(ILocalFile lcf)
            {
                return new LocalContentFileEntry(lcf.RelativeName, DateTime.UtcNow, false, lcf.CreationTimeUtc, lcf.LastWriteTimeUtc);
            }


            public sealed record LocalContentFileEntry(string RelativeName, DateTime Version, bool IsDeleted, DateTime? CreationTimeUtc, DateTime? LastWriteTimeUtc);


            private class AriusManifestEntryEqualityComparer : IEqualityComparer<LocalContentFileEntry>
            {
                public AriusManifestEntryEqualityComparer(bool ignoreCreationTimeLastWriteTime)
                {
                    _ignoreCreationTimeLastWriteTime = ignoreCreationTimeLastWriteTime;
                }

                private readonly bool _ignoreCreationTimeLastWriteTime;

                public bool Equals(LocalContentFileEntry x, LocalContentFileEntry y)
                {
                    return x.RelativeName == y.RelativeName &&
                           //x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version
                           x.IsDeleted == y.IsDeleted &&
                           (_ignoreCreationTimeLastWriteTime || x.CreationTimeUtc.Equals(y.CreationTimeUtc)) &&
                           (_ignoreCreationTimeLastWriteTime || x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc));
                }

                public int GetHashCode(LocalContentFileEntry obj)
                {
                    if (_ignoreCreationTimeLastWriteTime)
                        return HashCode.Combine(obj.RelativeName,
                            //obj.Version,  //DO NOT Compare on DateTime Version
                            obj.IsDeleted);
                            //obj.CreationTimeUtc, // Do NOT compare on CreationTime
                            //obj.LastWriteTimeUtc); // Do NOT compare on LastWriteTime
                    else
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