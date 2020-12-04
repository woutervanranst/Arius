using System;
using System.Linq;
using System.Reflection;
using Arius.Extensions;
using Arius.Services;
using Azure.Storage.Blobs.Models;

namespace Arius.Models
{
    internal abstract class Blob : IBlob
    {
        protected Blob(IRepository root, BlobItem blobItem, Func<IBlob, HashValue> hashValueProvider)
        {
            _bi = blobItem;

            _hash = new Lazy<HashValue>(() => hashValueProvider(this)); //NO method groep > moet lazily evaluated zijn
        }

        private readonly BlobItem _bi;
        private readonly Lazy<HashValue> _hash;


        public string FullName => _bi.Name;
        public string Name => _bi.Name.Split('/').Last(); //TODO werkt titi met alle soorten repos?
        public string NameWithoutExtension => Name.TrimEnd(this.GetType().GetCustomAttribute<ExtensionAttribute>()!.Extension);
        public HashValue Hash => _hash.Value;
    }

    [Extension(".7z.arius")]
    internal class RemoteEncryptedChunkBlob : Blob, IRemoteEncryptedChunkBlobItem //, IRemote<IEncrypted<IChunk<ILocalContentFile>>>
    {
        public RemoteEncryptedChunkBlob(IRepository root, BlobItem blobItem, Func<IBlob, HashValue> hashValueProvider) : base(root, blobItem, hashValueProvider)
        {
        }

        //public IEncrypted<IChunk<ILocalContentFile>> GetRemoteObject()
        //{
        //    throw new NotImplementedException();
        //}
    }

    //[Extension(".manifest.7z.arius")]
    //internal class RemoteEncryptedManifestBlob : Blob //, IRemote<IEncrypted<IManifestFile>>
    //{
    //    public RemoteEncryptedManifestBlob(string blobItemName) : base(blobItemName)
    //    {
    //    }

    //    //public IEncrypted<IManifestFile> GetRemoteObject()
    //    //{
    //    //    throw new NotImplementedException();
    //    //}
    //}
}