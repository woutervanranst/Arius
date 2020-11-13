using System;
using System.IO;

namespace Arius
{
    internal class AriusPointerFile : AriusFile, ILocalFile //TODO ILocalFile to LocalFile : AriusFile
    {
        /// <summary>
        /// Create a pointer for a local file with a remote manifest (that is already uploaded)
        /// </summary>
        /// <param name="lcf"></param>
        /// <param name="f"></param>
        /// <returns></returns>
        public static AriusPointerFile Create(AriusRootDirectory root, LocalContentFile lcf, RemoteEncryptedAriusManifest f)
        {
            return Create(root, lcf.AriusPointerFileFullName, f.Name);
        }

        private static AriusPointerFile Create(AriusRootDirectory root, string ariusPointerFullName, string encryptedManifestName)
        {
            if (File.Exists(ariusPointerFullName))
                throw new ArgumentException("The Pointer file already exists"); //TODO i  expect issies here when the binnary is changed?

            if (!encryptedManifestName.EndsWith(".manifest.7z.arius"))
                throw new ArgumentException("Not a valid encrypted manifest file name");

            File.WriteAllText(ariusPointerFullName, encryptedManifestName);
            return new AriusPointerFile(root, new FileInfo(ariusPointerFullName), encryptedManifestName);
        }

        public static AriusPointerFile FromFile(AriusRootDirectory root, FileInfo fi)
        {
            if (!fi.Exists)
                throw new ArgumentException("The Pointer file does not exist");

            return new AriusPointerFile(root, fi);
        }


        private AriusPointerFile(AriusRootDirectory root, FileInfo fi, string encryptedManifestName) : base(fi)
        {
            _root = root;
            _encryptedManifestName = new Lazy<string>(() => encryptedManifestName);
        }
        private AriusPointerFile(AriusRootDirectory root, FileInfo fi) : base(fi)
        {
            _root = root;
            _encryptedManifestName = new Lazy<string>(() => File.ReadAllText(fi.FullName));
        }

        private readonly AriusRootDirectory _root;

        public string EncryptedManifestName => _encryptedManifestName.Value;
        private readonly Lazy<string> _encryptedManifestName;

        
        // --- ILocalFile IMPLEMENTATIONS

        /// <summary>
        /// The Relative Name of the would-be LocalContentFile
        /// </summary>
        public string RelativeName => Path.GetRelativePath(_root.FullName, FullName).TrimEnd(".arius");
        public DateTime? CreationTimeUtc => null;
        public DateTime? LastWriteTimeUtc => null;

        
        // --- OTHER OVERRIDES
        public override string ToString() => base.FullName; //TODO Beter relativeName
        
    }
}
