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

        public override string Hash => _bi.Name.Substring(0, _bi.Name.Length - ".manifest.7z.arius".Length);



        private class AriusManifest
        {
            public string AsJson() =>
                JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true });

            public static AriusManifest FromJson(string json) => JsonSerializer.Deserialize<AriusManifest>(json);


            //private string AriusManifestName => $"{Hash}.manifest.arius";
            //public string EncryptedAriusManifestName => GetEncryptedAriusManifestBlobName(Hash);

            //public static string GetHash(string encryptedAriusManifestBlobName)
            //{
            //    if (!encryptedAriusManifestBlobName.EndsWith(".manifest.7z.arius"))
            //        throw new ArgumentException("NOT A MANIFEST"); //TODO


            //}
            



            [JsonConstructor]
            public AriusManifest(IEnumerable<AriusManifestEntry> entries, IEnumerable<string> encryptedChunks, string hash)
            {
                _localContentFiles = entries.ToList();
                EncryptedChunks = encryptedChunks;
                Hash = hash;
            }

            [JsonInclude]
            public IEnumerable<AriusManifestEntry> LocalContentFiles => _localContentFiles;
            private readonly List<AriusManifestEntry> _localContentFiles;
            [JsonInclude]
            public IEnumerable<string> EncryptedChunks { get; private set; }

            /// <summary>
            /// Hash of the unencrypted LocalContentFiles
            /// </summary>
            [JsonInclude]
            public string Hash { get; private set; }


            public void AddEntry(LocalContentFile lcf)
            {
                var me = GetAriusManifestEntry(lcf);

                if (!_localContentFiles.Contains(me, new AriusManifestEntryEqualityComparer()))
                    _localContentFiles.Add(me);
            }








            public static AriusManifestEntry GetAriusManifestEntry(LocalContentFile lcf)
            {
                return new AriusManifestEntry(lcf.RelativeName, DateTime.UtcNow, false, lcf.CreationTimeUtc, lcf.LastWriteTimeUtc);
            }

            public record AriusManifestEntry(string RelativeName, DateTime Version, bool IsDeleted,
                DateTime CreationTimeUtc, DateTime LastWriteTimeUtc);


            internal class AriusManifestEntryEqualityComparer : IEqualityComparer<AriusManifestEntry>
            {
                public bool Equals(AriusManifestEntry x, AriusManifestEntry y)
                {
                    return x.RelativeName == y.RelativeName &&
                           //x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version
                           x.IsDeleted == y.IsDeleted &&
                           x.CreationTimeUtc.Equals(y.CreationTimeUtc) &&
                           x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc);
                }

                public int GetHashCode(AriusManifestEntry obj)
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


//        AriusManifestFile : AriusFile //TODO deze clas s kan ertussenuit tss Manifest - ManifestFile - EncryptedManifestFile
//    {
//        public AriusManifestFile(FileInfo ariusManifestFile) : base(ariusManifestFile)
//        {
//        }

//    }

//    //internal interface IManifestFile
//    //{
//    //    /// <summary>
//    //    /// The name (with extension) of the manifest file
//    //    /// </summary>
//    //    string Name { get; }
//    //}

//    internal class EncryptedAriusManifestFile : AriusFile //, IManifestFile
//    {
//        public EncryptedAriusManifestFile(FileInfo encryptedAriusManifestFileInfo) : base(encryptedAriusManifestFileInfo)
//        {
//        }


//    internal class RemoteEncryptedAriusManifestFile //: IManifestFile
//    {


//        public IEnumerable<AriusPointerFile> CreatePointers(List<LocalContentFile> lcfs, string passphrase)
//        {

//            //Add the entries to the local content files
//            lcfs.ForEach(manifest.AddEntry);

//            //Create a new encrypted manifest
//            var tempAriusManifestFullName = Path.Combine(Path.GetTempPath(), lcfs.First().AriusManifestName);
//            var tempEncryptedAriusManifestFullName =
//                Path.Combine(Path.GetTempPath(), lcfs.First().EncryptedAriusManifestName);
//            manifest
//                .CreateAriusManifestFile(tempAriusManifestFullName)
//                .CreateEncryptedAriusManifestFile(tempEncryptedAriusManifestFullName, passphrase, true);

//            //Upload it
//            using var s = File.Open(tempEncryptedAriusManifestFullName, FileMode.Open, FileAccess.Read);
//            _bc.Upload(s, true);
//            s.Close();
//            File.Delete(tempEncryptedAriusManifestFullName);

//            //Generate the pointers to the manifest
//            var pointers = lcfs
//                .Select(lcf => AriusPointerFile.Create(lcf, this))
//                .ToImmutableArray();

//            return pointers;
//        }
//    }
//}














//internal class AriusManifest
//{

//    public static AriusManifest Create(LocalContentFile lcf, params EncryptedAriusChunk[] chunks)
//    {
//        return new AriusManifest(
//            new List<AriusManifestEntry> { AriusManifestEntry.GetAriusManifestEntry(lcf) },
//            chunks.Select(c => c.Name),
//            lcf.Hash);
//    }



//    [JsonConstructor]
//    public AriusManifest(IEnumerable<AriusManifestEntry> entries, IEnumerable<string> encryptedChunks, string hash)
//    {
//        _entries = entries.ToList();
//        EncryptedChunks = encryptedChunks;
//        Hash = hash;
//    }

//    [JsonInclude] public IEnumerable<AriusManifestEntry> Entries => _entries;
//    private readonly List<AriusManifestEntry> _entries;
//    [JsonInclude] public IEnumerable<string> EncryptedChunks { get; private set; }
//    [JsonInclude] public string Hash { get; private set; }


//    public AriusManifestFile CreateAriusManifestFile(string ariusManifestFullName)
//    {
//        return AriusManifestFile.Create(ariusManifestFullName, this);
//    }

//    public void AddEntry(LocalContentFile lcf)
//    {
//        var me = AriusManifestEntry.GetAriusManifestEntry(lcf);

//        if (!_entries.Contains(me, new AriusManifestEntryEqualityComparer()))
//            _entries.Add(me);
//    }


//    public string AsJson() =>
//        JsonSerializer.Serialize(this,
//            new JsonSerializerOptions { WriteIndented = true }); // TODO waarom niet gewoon Serialize(this)

//    public static AriusManifest FromJson(string json) => JsonSerializer.Deserialize<AriusManifest>(json);

//    public struct AriusManifestEntry
//    {
//        public string RelativeName { get; set; }
//        public DateTime Version { get; set; }
//        public bool IsDeleted { get; set; }
//        public DateTime CreationTimeUtc { get; set; }
//        public DateTime LastWriteTimeUtc { get; set; }

//        public static AriusManifestEntry GetAriusManifestEntry(LocalContentFile lcf)
//        {
//            return new AriusManifestEntry
//            {
//                RelativeName = lcf.RelativeName,
//                Version = DateTime.UtcNow,
//                IsDeleted = false,
//                CreationTimeUtc = lcf.CreationTimeUtc,
//                LastWriteTimeUtc = lcf.LastWriteTimeUtc,
//            };
//        }
//    }

//    public class AriusManifestEntryEqualityComparer : IEqualityComparer<AriusManifestEntry>
//    {
//        public bool Equals(AriusManifestEntry x, AriusManifestEntry y)
//        {
//            return x.RelativeName == y.RelativeName &&
//                   //x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version
//                   x.IsDeleted == y.IsDeleted &&
//                   x.CreationTimeUtc.Equals(y.CreationTimeUtc) &&
//                   x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc);
//        }

//        public int GetHashCode(AriusManifestEntry obj)
//        {
//            return HashCode.Combine(obj.RelativeName,
//                //obj.Version,  //DO NOT Compare on DateTime Version
//                obj.IsDeleted,
//                obj.CreationTimeUtc,
//                obj.LastWriteTimeUtc);
//        }
//    }

//}



///// <summary>
///// De unencrypted manifest file
///// </summary>
//internal class
//    AriusManifestFile : AriusFile //TODO deze clas s kan ertussenuit tss Manifest - ManifestFile - EncryptedManifestFile
//{
//    public static AriusManifestFile Create(string ariusManifestFullName, AriusManifest ariusManifest)
//    {
//        var json = ariusManifest.AsJson();
//        File.WriteAllText(ariusManifestFullName, json);

//        var fi = new FileInfo(ariusManifestFullName);
//        return new AriusManifestFile(fi);
//    }

//    public AriusManifest GetManifest()
//    {
//        var json = File.ReadAllText(this.FullName);
//        var ariusManifest = AriusManifest.FromJson(json);

//        return ariusManifest;
//    }

//    public AriusManifestFile(FileInfo ariusManifestFile) : base(ariusManifestFile)
//    {
//    }

//    public EncryptedAriusManifestFile CreateEncryptedAriusManifestFile(string encryptedAriusManifestFileFullName,
//        string passphrase, bool deleteUnencryptedManifestFile)
//    {
//        var eamf = EncryptedAriusManifestFile.Create(encryptedAriusManifestFileFullName, this, passphrase);
//        if (deleteUnencryptedManifestFile)
//            base.Delete();

//        return eamf;
//    }
//}

////internal interface IManifestFile
////{
////    /// <summary>
////    /// The name (with extension) of the manifest file
////    /// </summary>
////    string Name { get; }
////}

//internal class EncryptedAriusManifestFile : AriusFile //, IManifestFile
//{
//    public EncryptedAriusManifestFile(FileInfo encryptedAriusManifestFileInfo) : base(encryptedAriusManifestFileInfo)
//    {
//    }

//    public static EncryptedAriusManifestFile Create(string encryptedAriusManifestFileFullName,
//        AriusManifestFile ariusManifestFile, string passphrase)
//    {
//        var szu = new SevenZipUtils();
//        szu.EncryptFile(ariusManifestFile.FullName, encryptedAriusManifestFileFullName, passphrase,
//            CompressionLevel.Normal);

//        return new EncryptedAriusManifestFile(new FileInfo(encryptedAriusManifestFileFullName));
//    }

//    public AriusPointerFile CreatePointerFile(LocalContentFile lcf)
//    {
//        return AriusPointerFile.Create(lcf, this);
//    }

//    public AriusManifest GetAriusManifest(string passphrase)
//    {
//        var szu = new SevenZipUtils();
//        var tempDecryptedAriusManifestFileFullName = Path.Combine(Path.GetTempPath(), Name);
//        szu.DecryptFile(this.FullName, tempDecryptedAriusManifestFileFullName, passphrase);

//        var ariusManifestFile = new AriusManifestFile(new FileInfo(tempDecryptedAriusManifestFileFullName));
//        var manifest = ariusManifestFile.GetManifest();
//        File.Delete(tempDecryptedAriusManifestFileFullName);

//        return manifest;
//    }

//    /// <summary>
//    /// The name (with extension) of the manifest file
//    /// </summary>
//    public new string Name => base.Name;

//    public override string ToString() => base.Name;
//}


//internal class RemoteEncryptedAriusManifestFile //: IManifestFile
//{
//    public static RemoteEncryptedAriusManifestFile Create(BlobClient bc)
//    {
//        return new RemoteEncryptedAriusManifestFile(bc);
//    }

//    private RemoteEncryptedAriusManifestFile(BlobClient bc)
//    {
//        _bc = bc;
//    }

//    private readonly BlobClient _bc;

//    /// <summary>
//    /// The name (with extension) of the manifest file
//    /// </summary>
//    public string Name => _bc.Name;

//    /// <summary>
//    /// Hash of the unencrypted LocalContentFile
//    /// </summary>
//    public string Hash => GetHash(_bc.Name);


//    public IEnumerable<AriusPointerFile> CreatePointers(List<LocalContentFile> lcfs, string passphrase)
//    {
//        //Download the existing EncryptedManifest
//        var tempEncryptedAriusManifestFileFullname = Path.Combine(Path.GetTempPath(), Name);
//        _bc.DownloadTo(tempEncryptedAriusManifestFileFullname);

//        //Get the decrypted manifest
//        var eamf = new EncryptedAriusManifestFile(new FileInfo(tempEncryptedAriusManifestFileFullname));
//        var manifest = eamf.GetAriusManifest(passphrase);
//        File.Delete(tempEncryptedAriusManifestFileFullname);

//        //Add the entries to the local content files
//        lcfs.ForEach(manifest.AddEntry);

//        //Create a new encrypted manifest
//        var tempAriusManifestFullName = Path.Combine(Path.GetTempPath(), lcfs.First().AriusManifestName);
//        var tempEncryptedAriusManifestFullName =
//            Path.Combine(Path.GetTempPath(), lcfs.First().EncryptedAriusManifestName);
//        manifest
//            .CreateAriusManifestFile(tempAriusManifestFullName)
//            .CreateEncryptedAriusManifestFile(tempEncryptedAriusManifestFullName, passphrase, true);

//        //Upload it
//        using var s = File.Open(tempEncryptedAriusManifestFullName, FileMode.Open, FileAccess.Read);
//        _bc.Upload(s, true);
//        s.Close();
//        File.Delete(tempEncryptedAriusManifestFullName);

//        //Generate the pointers to the manifest
//        var pointers = lcfs
//            .Select(lcf => AriusPointerFile.Create(lcf, this))
//            .ToImmutableArray();

//        return pointers;
//    }
//}
//}