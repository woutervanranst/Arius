using System;
using System.Linq;
using Arius.Core.Extensions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.Models
{
    internal abstract class BlobBase //: IWithHashValue
    {
        /// <summary>
        /// Full Name (with path and extension)
        /// </summary>
        public abstract string FullName { get; }

        /// <summary>
        /// Name (with extension, without path)
        /// </summary>
        public string Name => FullName.Split(BlobFolderSeparatorChar).Last(); //TODO werkt dit met alle soorten repos?

        /// <summary>
        /// The Folder where this Blob resides
        /// </summary>
        public string Folder => FullName.Split(BlobFolderSeparatorChar).First(); //TODO quid if in the root?

        /// <summary>
        /// Length (in bytes) of the Blob
        /// </summary>
        public abstract long Length { get; }

        public abstract Hash Hash { get; }

        private const char BlobFolderSeparatorChar = '/';
    }



    internal class ManifestBlob : BlobBase
    {
        public ManifestBlob(BlobItem bi)
        {
            this.bi = bi;
        }
        protected readonly BlobItem bi;

        public override ManifestHash Hash => new(Name);
        public override string FullName => bi.Name;
        public override long Length => bi.Properties.ContentLength!.Value;
    }



    internal abstract class ChunkBlobBase : BlobBase
    {
        public static ChunkBlobItem GetChunkBlob(BlobItem bi)
        {
            return new ChunkBlobItem(bi);
        }
        public static ChunkBlobClient GetChunkBlob(BlobClient bc)
        {
            return new ChunkBlobClient(bc);
        }

        public abstract AccessTier AccessTier { get; }

        public static readonly string Extension = ".7z.arius";
        public bool Downloadable => AccessTier == AccessTier.Hot || AccessTier == AccessTier.Cool;
        public override ChunkHash Hash => new(Name.TrimEnd(Extension));
    }

    internal class ChunkBlobItem : ChunkBlobBase
    {
        internal ChunkBlobItem(BlobItem bi)
        {
            this.bi = bi;
        }
        private readonly BlobItem bi;


        public override long Length => bi.Properties.ContentLength!.Value;
        public override AccessTier AccessTier => bi.Properties.AccessTier!.Value;
        public override string FullName => bi.Name;
    }

    internal class ChunkBlobClient : ChunkBlobBase
    {
        internal ChunkBlobClient(BlobClient bc)
        {
            try
            {
                props = bc.GetProperties().Value;
                FullName = bc.Name;
            }
            catch (Azure.RequestFailedException)
            {
                throw new ArgumentException($"Blob {bc.Uri} not found. Either this is expected (no hydrated blob found) or the archive integrity is compromised?");
            }
        }
        private readonly BlobProperties props;


        public override long Length => props.ContentLength;

        public override AccessTier AccessTier => props.AccessTier switch
        {
            "Hot" => AccessTier.Hot,
            "Cool" => AccessTier.Cool,
            "Archive" => AccessTier.Archive,
            _ => throw new ArgumentException($"AccessTier not an expected value (is: {props.AccessTier}"),
        };

        public override string FullName { get; }
    }
}
