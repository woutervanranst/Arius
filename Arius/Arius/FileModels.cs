using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    //public abstract class File : IFile
    //{
    //    public abstract string FullName { get; }
    //    public abstract string Name { get; }
    //}

    internal abstract class LocalFile : ILocalFile //, IFile
    {
        protected LocalFile(LocalRootDirectory root, FileInfo fi, IHashValueProvider hashValueProvider)
        {
            if (!fi.Exists)
                throw new ArgumentException("The LocalFile does not exist");

            _fi = fi;

            _hash = new Lazy<HashValue>(() => hashValueProvider.GetHashValue(this)); //NO method groep > moet lazily evaluated zijn
        }

        protected readonly Lazy<HashValue> _hash;

        private readonly FileInfo _fi;

        public HashValue Hash => _hash.Value;

        public string FullName => _fi.FullName;
        public string Name => _fi.Name;
        public string DirectoryName => _fi.DirectoryName;
    }

    [Extension(".arius.pointer")]
    internal class LocalPointerFile : LocalFile, IPointerFile<IRemote<IEncrypted<IManifestFile>>>
    {
        public LocalPointerFile(LocalRootDirectory root, FileInfo fi, IHashValueProvider hashValueProvider) : base(root, fi, hashValueProvider)
        {
            _objectName = new Lazy<string>(() => File.ReadAllText(fi.FullName));
        }


        /// <summary>
        /// The name of the object that this pointer is pointing to
        /// </summary>
        /// <returns></returns>
        public string GetObjectName() => _objectName.Value;

        private readonly Lazy<string> _objectName;

        /// <summary>
        /// The object that this pointer is pointing to
        /// </summary>
        /// <returns></returns>
        public IRemote<IEncrypted<IManifestFile>> GetObject()
        {
            return null;
            //throw new NotImplementedException();
        }
    }

    [Extension(".*", true)]
    internal class LocalContentFile : LocalFile, ILocalContentFile, IChunk<ILocalContentFile>
    {
        public LocalContentFile(LocalRootDirectory root, FileInfo fi, IHashValueProvider hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }

    [Extension(".7z.arius")]
    internal class EncryptedLocalContentFile : LocalFile, IEncrypted<IFile>, IEncrypted<IChunk<IFile>>, IEncrypted<IChunk<LocalContentFile>>  //TODO clean up this type mess
    {
        public EncryptedLocalContentFile(LocalRootDirectory root, FileInfo fi, IHashValueProvider hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }


    internal abstract class Blob : IBlob
    {
        protected Blob(BlobItem bi)
        {
            _bi = bi;
        }

        private readonly BlobItem _bi;


        public string Name => _bi.Name;
        
    }

    //internal  class ManifestBlob : IRemoteManifestBlob
    //{
    //}

    //internal class ContentBlob : IRemoteContentBlob
    //{
    //}

    [Extension(".manifest.7z.arius")]
    class RemoteEncryptedManifestBlob : Blob, IRemote<IEncrypted<IManifestFile>>
    {
        public RemoteEncryptedManifestBlob(BlobItem bi) : base(bi)
        {
        }

        public IEncrypted<IManifestFile> GetRemoteObject()
        {
            throw new NotImplementedException();
        }
    }
}