using System;
using System.Collections.Generic;
using System.IO;
using Arius.Extensions;

namespace Arius.Models
{
    public interface IAriusEntry
    {
        public string RelativePath { get; }
        //public string Name { get; }
        public string ContentName { get; }
    }
    internal interface IWithHashValue
    {
        public HashValue Hash { get; }
    }
    internal interface IAriusEntryWithHash : IAriusEntry, IWithHashValue
    {
    }


    internal interface IFile
    {
        public string FullName { get; }
        public string Name { get; }
        DirectoryInfo Directory { get; }
        public long Length { get; }
        public void Delete();
    }




    public abstract class FileBase : IFile, IWithHashValue
    {
        protected FileBase(FileInfo fi)
        {
            _fi = fi;
        }
        protected readonly FileInfo _fi;

        public string FullName => _fi.FullName;
        public string Name => _fi.Name;
        public DirectoryInfo Directory => _fi.Directory;

        /// <summary>
        /// Size in bytes
        /// </summary>
        public long Length => _fi.Length;

        public void Delete()
        {
            _fi.Delete();
        }

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
    }

    public abstract class RelativeFileBase : FileBase
    {
        private readonly DirectoryInfo _root;

        protected RelativeFileBase(DirectoryInfo root, FileInfo fi) : base(fi)
        {
            _root = root;
        }

        public string RelativeName => Path.GetRelativePath(_root.FullName, _fi.FullName);
        public string RelativePath => Path.GetRelativePath(_root.FullName, _fi.DirectoryName);
        public DirectoryInfo Root => _root;
    }

    public abstract class RelativeAriusFileBase : RelativeFileBase, IAriusEntryWithHash
    {
        protected RelativeAriusFileBase(DirectoryInfo root, FileInfo fi) : base(root, fi)
        {
        }

        public abstract string ContentName { get; }
    }





    internal interface IChunkFile : IFile, IWithHashValue
    {
    }
    internal interface IEncryptedFile : IFile
    {
    }




    public class PointerFile : RelativeAriusFileBase, IAriusEntryWithHash
    {
        public const string Extension = ".pointer.arius";

        //public PointerFile(FileInfo fi) : this(fi.Directory, fi)
        //{
        //  DO NOT IMPLEMENT THIS, IT WILL CAUSE CONFUSION & BUGS & INVALID ARCHIVES
        //}

        /// <summary>
        /// Create a new PointerFile with the given root and read the Hash from the file
        /// </summary>
        public PointerFile(DirectoryInfo root, FileInfo fi) : base(root, fi)
        {
            this.Hash = new HashValue() { Value = File.ReadAllText(fi.FullName) };
        }

        internal FileInfo BinaryFileInfo => new FileInfo(_fi.FullName.TrimEnd(Extension));

        internal IEnumerable<HashValue> ChunkHashes { get; set; }

        public override string ContentName => Name.TrimEnd(Extension);
    }

    public class BinaryFile : RelativeAriusFileBase, IAriusEntryWithHash, IChunkFile
    {
        public BinaryFile(DirectoryInfo root, FileInfo fi) : base(root, fi) { }

        internal IEnumerable<IChunkFile> Chunks { get; set; }
        //public HashValue? ManifestHash { get; set; }
        //public bool Uploaded { get; set; }

        public FileInfo PointerFileInfo => new FileInfo(_fi.FullName + PointerFile.Extension);
        public override string ContentName => Name;
    }

    internal class ChunkFile : FileBase, IChunkFile
    {
        public const string Extension = ".chunk.arius";

        public ChunkFile(FileInfo fi, HashValue hash) : base(fi)
        {
            base.Hash = hash;
        }
    }

    internal class EncryptedChunkFile : FileBase, IEncryptedFile, IChunkFile
    {
        public const string Extension = ".7z.arius";

        public EncryptedChunkFile(FileInfo fi, HashValue hash) : base(fi)
        {
            base.Hash = hash;
        }
    }
}