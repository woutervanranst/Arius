using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Arius.Extensions;
using Arius.Services;

namespace Arius.Models
{
    internal abstract class AriusArchiveItem : IFileWithHash
    {
        protected readonly FileInfo _fi;

        protected AriusArchiveItem(FileInfo fi)
        {
            _fi = fi;
        }

        public string FullName => _fi.FullName;
        public string Name => _fi.Name;
        public DirectoryInfo Directory => _fi.Directory;

        public HashValue Hash
        {
            get => _hashValue.Value;
            set
            {
                if (_hashValue.HasValue)
                    throw new InvalidOperationException("CAN ONLY BE SET ONCE");
                _hashValue = value;
            }
        }
        private HashValue? _hashValue;

        public void Delete()
        {
            _fi.Delete();
        }
    }

    internal class PointerFile : AriusArchiveItem
    {
        public const string Extension = ".pointer.arius";

        public PointerFile(FileInfo fi) : base(fi) { }

        public PointerFile(FileInfo fi, HashValue manifestHash) : base(fi)
        {
            this.Hash = manifestHash;
        }
    }

    internal class BinaryFile : AriusArchiveItem, IChunkFile
    {
        public BinaryFile(FileInfo fi) : base(fi) { }

        public IEnumerable<IChunkFile> Chunks { get; set; }
        public HashValue? ManifestHash { get; set; }
        public bool Uploaded { get; set; }
    }

    internal class ChunkFile : AriusArchiveItem, IChunkFile
    {
        public const string Extension = ".chunk.arius";

        public ChunkFile(FileInfo fi) : base(fi) { }

        //public EncryptedChunkFile2 EncryptedChunkFile { get; set; }
        public bool Uploaded { get; set; }
    }

    internal class EncryptedChunkFile : AriusArchiveItem, IEncryptedFile, IChunkFile
    {
        public const string Extension = ".7z.arius";

        public EncryptedChunkFile(FileInfo fi, HashValue hash) : base(fi)
        {
            base.Hash = hash;
        }

        /// <summary>
        /// Size in Bytes
        /// </summary>
        public long Length => _fi.Length;

        public bool Uploaded { get; set; }
    }

}