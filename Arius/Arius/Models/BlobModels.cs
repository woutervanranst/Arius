using System.Linq;
using Arius.Extensions;
using Arius.Repositories;
using Azure.Storage.Blobs.Models;

namespace Arius.Models
{
    internal abstract class Blob
    {
        protected Blob(
            //IRepository root, 
            BlobItem blobItem //, 
            //Func<IBlob, HashValue> hashValueProvider
        )
        {
            //_root = root;
            _bi = blobItem;

            //_hash = new Lazy<HashValue>(() => hashValueProvider(this)); //NO method groep > moet lazily evaluated zijn
        }

        //protected readonly IRepository _root;
        protected readonly BlobItem _bi;
        //private readonly Lazy<HashValue> _hash;


        public string FullName => _bi.Name;
        public string Name => _bi.Name.Split('/').Last(); //TODO werkt titi met alle soorten repos?
        public string Folder => _bi.Name.Split('/').First();
        //protected string NameWithoutExtension => Name.TrimEnd(Extension);
        public abstract HashValue Hash { get; }
        //protected abstract string Extension { get; }
    }

    internal class RemoteEncryptedChunkBlobItem : Blob
    {
        public RemoteEncryptedChunkBlobItem(BlobItem bi) : base(bi)
        {
        }

        public override HashValue Hash => new HashValue {Value = Name.TrimEnd(Extension)};
        protected string Extension => ".7z.arius";
        public long Length => _bi.Properties.ContentLength!.Value;
        public AccessTier AccessTier => _bi.Properties.AccessTier!.Value;
        public bool Downloadable => AccessTier == AccessTier.Hot || AccessTier == AccessTier.Cool;
        public BlobItem BlobItem => _bi;
    }

    internal class RemoteManifestBlobItem : Blob
    {
        public RemoteManifestBlobItem(BlobItem bi) : base(bi)
        {
        }

        public override HashValue Hash => new HashValue { Value = Name };
    }
}
