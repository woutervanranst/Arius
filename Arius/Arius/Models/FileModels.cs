using System;
using System.Collections.Generic;
using System.IO;
using Arius.Extensions;

namespace Arius.Models
{
    internal abstract class AriusArchiveItem : IFileWithHash
    {
        private readonly DirectoryInfo _root;
        protected readonly FileInfo _fi;

        protected AriusArchiveItem(DirectoryInfo root, FileInfo fi)
        {
            _root = root;
            _fi = fi;
        }

        public string FullName => _fi.FullName;
        public string RelativeName => Path.GetRelativePath(_root.FullName, _fi.FullName);
        public string Name => _fi.Name;
        public DirectoryInfo Directory => _fi.Directory;
        public DirectoryInfo Root => _root;

        /// <summary>
        /// Size in bytes
        /// </summary>
        public long Length => _fi.Length;

        public HashValue Hash
        {
            get => _hashValue!.Value;
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

        /// <summary>
        /// Create a new PointerFile and read the Hash from the file
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fi"></param>
        public PointerFile(DirectoryInfo root, FileInfo fi) : base(root, fi)
        {
            this.Hash = new HashValue() { Value = File.ReadAllText(fi.FullName) };
        }

        public FileInfo BinaryFileInfo => new FileInfo(_fi.FullName.TrimEnd(Extension));

        public IEnumerable<HashValue> ChunkHashes { get; set; }
    }

    internal class BinaryFile : AriusArchiveItem, IChunkFile
    {
        public BinaryFile(DirectoryInfo root, FileInfo fi) : base(root, fi) { }

        public IEnumerable<IChunkFile> Chunks { get; set; }
        //public HashValue? ManifestHash { get; set; }
        //public bool Uploaded { get; set; }

        public FileInfo PointerFileInfo => new FileInfo(_fi.FullName + PointerFile.Extension);
    }

    internal class ChunkFile : AriusArchiveItem, IChunkFile
    {
        public const string Extension = ".chunk.arius";

        public ChunkFile(DirectoryInfo root, FileInfo fi, HashValue hash) : base(root, fi)
        {
            base.Hash = hash;
        }

        public bool Uploaded { get; set; }
    }

    internal class EncryptedChunkFile : AriusArchiveItem, IEncryptedFile, IChunkFile
    {
        public const string Extension = ".7z.arius";

        public EncryptedChunkFile(DirectoryInfo root, FileInfo fi, HashValue hash) : base(root, fi)
        {
            base.Hash = hash;
        }

        /// <summary>
        /// Size in Bytes
        /// </summary>
        //public long Length => _fi.Length;

        public bool Uploaded { get; set; }
    }
}