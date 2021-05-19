﻿using System;
using System.Linq;
using Arius.Extensions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Arius.Models
{
    internal abstract class BlobBase : IWithHashValue
    {
        public abstract string FullName { get; }
        public string Name => FullName.Split(BlobFolderSeparatorChar).Last(); //TODO werkt dit met alle soorten repos?
        public string Folder => FullName.Split(BlobFolderSeparatorChar).First();
        public abstract long Length { get; }
        public abstract HashValue Hash { get; }

        private const char BlobFolderSeparatorChar = '/';
    }



    internal abstract class BlobItemBase : BlobBase
    {
        protected BlobItemBase(BlobItem blobItem)
        {
            bi = blobItem;
        }
        protected readonly BlobItem bi;

        public override string FullName => bi.Name;
        //public override string Name => _bi.Name.Split(BlobFolderSeparatorChar).Last(); //TODO werkt dit met alle soorten repos?
        //public override string Folder => _bi.Name.Split(BlobFolderSeparatorChar).First();
        //public abstract HashValue Hash { get; }
        public override long Length => bi.Properties.ContentLength!.Value;
    }

    internal class RemoteManifestBlob : BlobItemBase //TODO rename naar ManifestBlob
    {
        public RemoteManifestBlob(BlobItem bi) : base(bi)
        {
        }

        public override HashValue Hash => new() { Value = Name };
        
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

        //protected RemoteEncryptedChunkBase(BlobItem bi) : base(bi)
        //{
        //}

        //public override HashValue Hash => new HashValue {Value = Name.TrimEnd(Extension)};
        //protected string Extension => ".7z.arius";
        //public long Length => _bi.Properties.ContentLength!.Value;
        //public AccessTier AccessTier => _bi.Properties.AccessTier!.Value;
        //public bool Downloadable => AccessTier == AccessTier.Hot || AccessTier == AccessTier.Cool;
        //public BlobItem BlobItem => _bi;

        public abstract AccessTier AccessTier { get; }

        public static readonly string Extension = ".7z.arius";
        public bool Downloadable => AccessTier == AccessTier.Hot || AccessTier == AccessTier.Cool;
        public override HashValue Hash => new() { Value = Name.TrimEnd(Extension) };
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
