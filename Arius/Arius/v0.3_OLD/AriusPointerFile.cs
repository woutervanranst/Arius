//using System;
//using System.IO;

//namespace Arius
//{
//    internal class AriusPointerFile : AriusFile
//    {
//        // --- STATIC CONSTRUCTORS

//        /// <summary>
//        /// Create a pointer for a local file with a remote manifest (that is already uploaded)
//        /// </summary>
//        /// <param name="lcf"></param>
//        /// <param name="manifest"></param>
//        /// <returns></returns>
//        public static AriusPointerFile Create(AriusRootDirectory root, LocalContentFile lcf, RemoteEncryptedAriusManifest manifest)
//        {
//            if (File.Exists(lcf.AriusPointerFileFullName))
//                throw new ArgumentException("The Pointer file already exists"); //TODO i  expect issies here when the binnary is changed?

//            if (!manifest.Name.EndsWith(".manifest.7z.arius"))
//                throw new ArgumentException("Not a valid encrypted manifest file name");

//            File.WriteAllText(lcf.AriusPointerFileFullName, manifest.Name);

//            File.SetCreationTimeUtc(lcf.AriusPointerFileFullName, File.GetCreationTimeUtc(lcf.FullName));
//            File.SetLastWriteTimeUtc(lcf.AriusPointerFileFullName, File.GetLastWriteTimeUtc(lcf.FullName));

//            return new AriusPointerFile(root, new FileInfo(lcf.AriusPointerFileFullName), manifest.Name);
//        }

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
