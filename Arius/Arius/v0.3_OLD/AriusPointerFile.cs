//using System;
//using System.IO;

//namespace Arius
//{
//    internal class AriusPointerFile : AriusFile
//    {
//        // --- STATIC CONSTRUCTORS


//        public static AriusPointerFile Create(AriusRootDirectory root, RemoteEncryptedAriusManifest.AriusManifest.AriusPointerFileEntry e, RemoteEncryptedAriusManifest manifest)
//        {
//            var fullName = Path.Combine(root.FullName, $"{e.RelativeName}.arius");

//            var fi = new FileInfo(fullName);
            
//            if (!fi.Directory.Exists)
//                fi.Directory.Create();

//            File.WriteAllText(fullName, manifest.Name);

//            fi.CreationTimeUtc = e.CreationTimeUtc!.Value;
//            fi.LastWriteTimeUtc = e.LastWriteTimeUtc!.Value;

//            return new AriusPointerFile(root, new FileInfo(fullName), manifest.Name);
//        }



//        // --- CONSTRUCTORS
//        private AriusPointerFile(AriusRootDirectory root, FileInfo fi, string encryptedManifestName) : base(fi)
//        {
//            _root = root;
//            _encryptedManifestName = new Lazy<string>(() => encryptedManifestName);
//        }


//        private readonly AriusRootDirectory _root;


//        // --- PROPERTIES

//        public string EncryptedManifestName => _encryptedManifestName.Value;
//        private readonly Lazy<string> _encryptedManifestName;

//        /// <summary>
//        /// The Relative Name of the would-be LocalContentFile
//        /// </summary>
//        public string RelativeLocalContentFileName => Path.GetRelativePath(_root.FullName, LocalContentFileFullName);

//        /// <summary>
//        /// The CreationTimeUtc of the LocalContentFile if it exists. Null otherwise.; 
//        /// </summary>
//        public DateTime CreationTimeUtc => File.GetCreationTimeUtc(FullName);

//        public DateTime LastWriteTimeUtc => File.GetLastWriteTimeUtc(FullName);

//        public string LocalContentFileFullName => _fi.FullName.TrimEnd(".arius");

//        // --- METHODS


//        // --- OTHER OVERRIDES
//        public override string ToString() => base.FullName; //TODO Beter relativeName
        
//    }
//}
