using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Arius
{
    internal abstract class LocalFile : ILocalFile //, IFile
    {

        protected LocalFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider)
        {
            if (!fi.Exists)
                throw new ArgumentException("The LocalFile does not exist");

            Root = root;

            _fi = fi;

            _hash = new Lazy<HashValue>(() => hashValueProvider(this)); //NO method groep > moet lazily evaluated zijn
        }

        protected readonly Lazy<HashValue> _hash;

        protected readonly FileInfo _fi;

        public HashValue Hash => _hash.Value;

        public string FullName => _fi.FullName;
        public string Name => _fi.Name;
        public string DirectoryName => _fi.DirectoryName;
        public IRepository Root { get; }

        public void Delete()
        {
            _fi.Delete();
        }

        public string NameWithoutExtension => Name.TrimEnd(this.GetType().GetCustomAttribute<ExtensionAttribute>().Extension);
    }

    [Extension(".arius.pointer")]
    internal class LocalPointerFile : LocalFile, IPointerFile
    {
        public LocalPointerFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
            _objectName = new Lazy<string>(() => File.ReadAllText(fi.FullName));
        }


        /// <summary>
        /// The name of the object that this pointer is pointing to
        /// </summary>
        /// <returns></returns>
        public string GetObjectName() => _objectName.Value;

        private readonly Lazy<string> _objectName;

        ///// <summary>
        ///// The object that this pointer is pointing to
        ///// </summary>
        ///// <returns></returns>
        //public IRemote<IEncrypted<IManifestFile>> GetObject()
        //{
        //    return null;
        //    //throw new NotImplementedException();
        //}
        public string RelativeContentName => Path.GetRelativePath(Root.FullName, Name);
        public DateTime CreationTimeUtc { get => _fi.CreationTimeUtc; set => _fi.CreationTimeUtc = value; }
        public DateTime LastWriteTimeUtc { get => _fi.LastWriteTimeUtc; set => _fi.LastWriteTimeUtc = value; }
    }

    [Extension(".*", true, encryptedType: typeof(RemoteEncryptedChunkBlob))]
    internal class LocalContentFile : LocalFile, ILocalContentFile, IChunkFile
    {
        public LocalContentFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }

        public string RelativeContentName => Path.GetRelativePath(Root.FullName, Name);
        public DateTime CreationTimeUtc { get => _fi.CreationTimeUtc; set => _fi.CreationTimeUtc = value; }
        public DateTime LastWriteTimeUtc { get => _fi.LastWriteTimeUtc; set => _fi.LastWriteTimeUtc = value; }
    }

    [Extension(".7z.arius")]
    internal class EncryptedChunkFile : LocalFile, IEncryptedChunkFile
    {
        public EncryptedChunkFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }




    [Extension(".manifest.arius")]
    internal class LocalManifestFile : LocalFile, IManifestFile //, IRemote<IEncrypted<IManifestFile>>
    {
        public LocalManifestFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }
    [Extension(".manifest.7z.arius", decryptedType: typeof(LocalManifestFile))]
    internal class LocalEncryptedManifestFile : LocalFile, IEncryptedManifestFile //, IRemote<IEncrypted<IManifestFile>>
    {
        public LocalEncryptedManifestFile(IRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }

    


}