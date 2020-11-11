using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace Arius
{
    internal class AriusPointerFile : AriusFile
    {
        /// <summary>
        /// Create a pointer for a local file with a remote manifest (that is already uploaded)
        /// </summary>
        /// <param name="lcf"></param>
        /// <param name="f"></param>
        /// <returns></returns>
        public static AriusPointerFile Create(LocalContentFile lcf, RemoteEncryptedAriusManifestFile f)
        {
            return Create(lcf.AriusPointerFileFullName, f.Name);
        }

        /// <summary>
        /// Create a pointer for a local file with a local manifest (that is not yet uploaded)
        /// </summary>
        /// <param name="lcf"></param>
        /// <param name="f"></param>
        /// <returns></returns>
        public static AriusPointerFile Create(LocalContentFile lcf, EncryptedAriusManifestFile f)
        {
            return Create(lcf.AriusPointerFileFullName, f.Name);
        }
        private static AriusPointerFile Create(string ariusPointerFullName, string encryptedManifestName)
        {
            if (File.Exists(ariusPointerFullName))
                throw new ArgumentException("The Pointer file already exists"); //TODO i  expect issies here when the binnary is changed?

            if (!encryptedManifestName.EndsWith(".manifest.7z.arius"))
                throw new ArgumentException("Not a valid encrypted manifest file name");

            File.WriteAllText(ariusPointerFullName, encryptedManifestName);
            return new AriusPointerFile(new FileInfo(ariusPointerFullName), encryptedManifestName);
        }

        public static AriusPointerFile Create(FileInfo fi)
        {
            if (!fi.Exists)
                throw new ArgumentException("The Pointer file does not exist");

            return new AriusPointerFile(fi);
        }


        private AriusPointerFile(FileInfo fi, string encryptedManifestName) : base(fi)
        {
            _encryptedManifestName = new Lazy<string>(() => encryptedManifestName);
        }
        private AriusPointerFile(FileInfo fi) : base(fi)
        {
            _encryptedManifestName = new Lazy<string>(() => File.ReadAllText(fi.FullName));
        }


        public string EncryptedManifestName => _encryptedManifestName.Value;
        private readonly Lazy<string> _encryptedManifestName;


        public override string ToString() => base.FullName; //TODO Beter relativeName
    }
}
