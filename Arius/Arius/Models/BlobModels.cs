using System.Linq;
using Arius.Extensions;
using Arius.Repositories;
using Azure.Storage.Blobs.Models;

namespace Arius.Models
{
    internal abstract class Blob : IWithHashValue // : IAriusArchiveItem
    {
        protected Blob(BlobItem blobItem)
        {
            _bi = blobItem;
        }
        
        protected readonly BlobItem _bi;

        public string FullName => _bi.Name;
        public string Name => _bi.Name.Split('/').Last(); //TODO werkt dit met alle soorten repos?
        public string Folder => _bi.Name.Split('/').First();
        public abstract HashValue Hash { get; }
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
