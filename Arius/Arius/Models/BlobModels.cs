using System;
using System.Linq;
using System.Reflection;
using Arius.Extensions;
using Arius.Repositories;
using Arius.Services;
using Azure.Storage.Blobs.Models;

namespace Arius.Models
{
    internal abstract class Blob : IBlob
    {
        protected Blob(IRepository root, BlobItem blobItem, Func<IBlob, HashValue> hashValueProvider)
        {
            _root = root;
            _bi = blobItem;

            _hash = new Lazy<HashValue>(() => hashValueProvider(this)); //NO method groep > moet lazily evaluated zijn
        }

        protected readonly IRepository _root;
        protected readonly BlobItem _bi;
        private readonly Lazy<HashValue> _hash;


        public string FullName => _bi.Name;
        public string Name => _bi.Name.Split('/').Last(); //TODO werkt titi met alle soorten repos?
        public string Folder => _bi.Name.Split('/').First();
        public string NameWithoutExtension => Name.TrimEnd(this.GetType().GetCustomAttribute<ExtensionAttribute>()!.Extension);
        public HashValue Hash => _hash.Value;
    }

    [Extension(".7z.arius")]
    internal class RemoteEncryptedChunkBlob : Blob, IRemoteEncryptedChunkBlobItem //, IRemote<IEncrypted<IChunk<ILocalContentFile>>>
    {
        public RemoteEncryptedChunkBlob(IRepository root, BlobItem blobItem, Func<IBlob, HashValue> hashValueProvider) : base(root, blobItem, hashValueProvider)
        {
            _hydratedBlob = new Lazy<IRemoteEncryptedChunkBlobItem>(() =>
            {
                var root = (RemoteEncryptedChunkRepository) _root;
                return root.GetHydratedChunkBlobItem(this);
            });
        }

        private readonly Lazy<IRemoteEncryptedChunkBlobItem> _hydratedBlob;


        public AccessTier AccessTier => _bi.Properties.AccessTier!.Value;
        public bool CanDownload()
        {
            return _hydratedBlob.Value != null;
        }

        public IRemoteEncryptedChunkBlobItem Hydrated => _hydratedBlob.Value;
    }
}