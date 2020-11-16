using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    public abstract class File : IFile
    {
        public abstract string FullName { get; }
        public abstract string Name { get; }
    }

    internal abstract class LocalFile : File, ILocalFile
    {
        protected LocalFile(FileInfo fi)
        {
            if (!fi.Exists)
                throw new ArgumentException("The LocalFile does not exist");

            _fi = fi;
        }

        private readonly FileInfo _fi;


        public override string FullName => _fi.FullName;
        public override string Name => _fi.Name;
    }

    internal class LocalPointerFile : LocalFile, IPointerFile<IRemoteManifestBlob>
    {
        public LocalPointerFile(LocalRootDirectory root, FileInfo fi, IHashValueProvider hashValueProvider) : base(fi)
        {
        }

        public IRemoteManifestBlob GetObject()
        {
            throw new NotImplementedException();
        }
    }

    internal class LocalContentFile : LocalFile, ILocalContentFile
    {

        public LocalContentFile(LocalRootDirectory root, FileInfo fi, IHashValueProvider hashValueProvider) : base(fi)
        {
            _hash = new Lazy<HashValue>(() => hashValueProvider.GetHashValue(this)); //NO method groep > moet lazily evaluated zijn
        }

        protected readonly Lazy<HashValue> _hash;

        public HashValue Hash => _hash.Value;
    }







    abstract class Blob : IBlob
    {
        protected Blob(BlobItem bi)
        {
            _bi = bi;
        }

        private readonly BlobItem _bi;


        public string Name => _bi.Name;
    }

    internal  class ManifestBlob : IRemoteManifestBlob
    {
        public string Name => throw new NotImplementedException();
    }

    internal class ContentBlob : IRemoteContentBlob
    {

    }

}
