using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        protected LocalFile(IRepository<ILocalFile> root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider)
        {
            if (!fi.Exists)
                throw new ArgumentException("The LocalFile does not exist");

            Root = root;

            _fi = fi;

            _hash = new Lazy<HashValue>(() => hashValueProvider(this)); //NO method groep > moet lazily evaluated zijn
        }

        protected readonly Lazy<HashValue> _hash;

        private readonly FileInfo _fi;

        public HashValue Hash => _hash.Value;

        public string FullName => _fi.FullName;
        public string Name => _fi.Name;
        //public string DirectoryName => _fi.DirectoryName;
        public IRepository<ILocalFile> Root { get; }

        public void Delete()
        {
            _fi.Delete();
        }

        public string FullNameWithoutExtension => FullName.TrimEnd(this.GetType().GetCustomAttribute<ExtensionAttribute>().Extension);
    }

    [Extension(".arius.pointer")]
    internal class LocalPointerFile : LocalFile, IPointerFile
    {
        public LocalPointerFile(IRepository<ILocalFile> root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
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
    }

    [Extension(".*", true)]
    internal class LocalContentFile : LocalFile, ILocalContentFile, IChunk
    {
        public LocalContentFile(IRepository<ILocalFile> root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }

    //[Extension(".7z.arius")]
    //internal class EncryptedLocalContentFile : LocalFile, IEncrypted, IChunk //TODO clean up this type mess
    //{
    //    public EncryptedLocalContentFile(ILocalRepository root, FileInfo fi, IHashValueProvider hashValueProvider) : base(root, fi, hashValueProvider)
    //    {
    //    }
    //}




    internal abstract class Blob : IBlob
    {
        //protected Blob(string blobItemName)
        //{
        //}
        protected Blob(string blobItemName)
        {
            //_bi = bi;

            //throw new NotImplementedException("TODO DE FACTORY EN REMOTEARCHIVE EN EXISTS()");
        }

        //private readonly BlobItem _bi;


        public string Name => "NAM"; // _bi.Name;
        public string FullNameWithoutExtension { get; }

        public string FullName => Name;
    }

    //internal  class ManifestBlob : IRemoteManifestBlob
    //{
    //}

    [Extension(".7z.arius")]
    internal class RemoteEncryptedContentBlob : Blob //, IRemote<IEncrypted<IChunk<ILocalContentFile>>>
    {
        //public RemoteEncryptedContentBlob(string blobItemName) : base(blobItemName)
        //{
        //}

        public RemoteEncryptedContentBlob(string blobItemName) : base(blobItemName)
        {
        }

        //public IEncrypted<IChunk<ILocalContentFile>> GetRemoteObject()
        //{
        //    throw new NotImplementedException();
        //}
    }

    [Extension(".manifest.7z.arius")]
    internal class RemoteEncryptedManifestBlob : Blob //, IRemote<IEncrypted<IManifestFile>>
    {
        public RemoteEncryptedManifestBlob(string blobItemName) : base(blobItemName)
        {
        }

        //public IEncrypted<IManifestFile> GetRemoteObject()
        //{
        //    throw new NotImplementedException();
        //}
    }

    [Extension(".manifest.7z.arius", decryptedType: typeof(LocalManifestFile))]
    internal class LocalEncryptedManifestFile : LocalFile, IEncryptedManifestFile //, IRemote<IEncrypted<IManifestFile>>
    {
        public LocalEncryptedManifestFile(IRepository<ILocalFile> root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }

    [Extension(".manifest.arius")]
    internal class LocalManifestFile : LocalFile, IManifestFile //, IRemote<IEncrypted<IManifestFile>>
    {
        public LocalManifestFile(IRepository<ILocalFile> root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
        {
        }
    }


}