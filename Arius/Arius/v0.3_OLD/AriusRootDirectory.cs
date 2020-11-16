//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;

//namespace Arius
//{
//    class AriusRootDirectory
//    {
//        public AriusRootDirectory(string path)
//        {
//            _root = new DirectoryInfo(path);
//        }

//        private readonly DirectoryInfo _root;

//        public string FullName => _root.FullName;

//        public IEnumerable<FileInfo> GetNonAriusFiles() => _root.GetFiles("*.*", SearchOption.AllDirectories).Where(fi => !fi.Name.EndsWith(".arius"));
//        public IEnumerable<FileInfo> GetAriusFiles() => _root.GetFiles("*.arius", SearchOption.AllDirectories);
//        public IEnumerable<AriusPointerFile> GetAriusPointerFiles() => GetAriusFiles().Select(fi => AriusPointerFile.FromFile(this, fi));

//        internal bool Exists(RemoteEncryptedAriusManifest.AriusManifest.AriusPointerFileEntry apfe)
//        {
//            return File.Exists(GetFullName(apfe));
//        }

//        internal string GetFullName(RemoteEncryptedAriusManifest.AriusManifest.AriusPointerFileEntry apfe)
//        {
//            return Path.Combine(FullName, apfe.RelativeName);
//        }
//    }
//}
