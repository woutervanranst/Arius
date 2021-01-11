using System;
using System.IO;
using System.Reflection;
using Arius.Extensions;
using Arius.Services;

namespace Arius.Models
{
    internal abstract class AriusArchiveItem : IFileWithHash
    {
        private readonly FileInfo _fi;

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

        public IChunkFile[] Chunks { get; set; }
    }

    internal class ChunkFile2 : AriusArchiveItem, IChunkFile
    {
        public const string Extension = ".chunk.arius";

        public ChunkFile2(FileInfo fi) : base(fi) { }

        //public EncryptedChunkFile2 EncryptedChunkFile { get; set; }
    }

    internal class EncryptedChunkFile2 : AriusArchiveItem, IEncryptedFile
    {
        public const string Extension = ".7z.arius";

        public EncryptedChunkFile2(FileInfo fi, HashValue hash) : base(fi)
        {
            base.Hash = hash;
        }
    }

}