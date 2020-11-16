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
        protected LocalFile(FileInfo fi, IHashValueProvider hashValueProvider)
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
    }

    internal class LocalPointerFile : LocalFile, IPointerFile<IRemote<IEncrypted<IManifestFile>>>
    {
        public LocalPointerFile(LocalRootDirectory root, FileInfo fi, IHashValueProvider hashValueProvider) : base(fi)
        {
        }

        public IRemote<IEncrypted<IManifestFile>> GetObject()
        {
            throw new NotImplementedException();
        }
    }

    internal class LocalContentFile : LocalFile, ILocalContentFile
    {

        public LocalContentFile(LocalRootDirectory root, FileInfo fi, IHashValueProvider hashValueProvider) : base(fi)
        {
            
        }

        
    }







    abstract class Blob : IBlob
    {
        protected Blob(BlobItem bi)
        {
            _bi = bi;
        }

        private readonly BlobItem _bi;


        public string Name => _bi.Name;
        //}

        //internal  class ManifestBlob : IRemoteManifestBlob
        //{
        //}

        //internal class ContentBlob : IRemoteContentBlob
        //{
        //}

        class RemoteEncryptedManifestfile : Blob, IRemote<IEncrypted<IManifestFile>>
        {
            public RemoteEncryptedManifestfile(BlobItem bi) : base(bi)
            {
            }

            public IEncrypted<IManifestFile> GetRemoteObject()
            {
                throw new NotImplementedException();
            }
        }
    }
