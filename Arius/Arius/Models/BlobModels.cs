using System.Linq;
using Arius.Extensions;
using Arius.Repositories;
using Arius.Services;
using Azure.Storage.Blobs.Models;

namespace Arius.Models
{
    internal abstract class Blob2
    {
        protected Blob2(
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
        public string NameWithoutExtension => Name.TrimEnd(Extension);
        public abstract HashValue Hash { get; }
        protected abstract string Extension { get; }
    }

    class RemoteEncryptedChunkBlobItem : Blob2
    {
        public RemoteEncryptedChunkBlobItem(BlobItem bi) : base(bi)
        {
        }

        public override HashValue Hash => new HashValue {Value = NameWithoutExtension};
        protected override string Extension => ".7z.arius";
    }
}
