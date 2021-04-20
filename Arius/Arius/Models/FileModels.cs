using System;
using System.Collections.Generic;
using System.IO;
using Arius.Extensions;

namespace Arius.Models
{
    public interface IAriusEntry
    {
        /// <summary>
        /// Path relative to the root
        /// </summary>
        public string RelativePath { get; }

        /// <summary>
        /// Name (without path but with Extension) of the (equivalent) BinaryFile (eg. 'myFile.bmp')
        /// </summary>
        public string ContentName { get; }
    }

    internal interface IWithHashValue
    {
        public HashValue Hash { get; }
    }

    /// <inheritdoc/>
    internal interface IAriusEntryWithHash : IAriusEntry, IWithHashValue
    {
    }


    internal interface IFile
    {
        /// <summary>
        /// Full Name (with path and extension)
        /// </summary>
        public string FullName { get; }

        /// <summary>
        /// Name (with extension)
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// Directory where this File resides
        /// </summary>
        DirectoryInfo Directory { get; }
        
        /// <summary>
        /// Length (in bytes) of the File
        /// </summary>
        public long Length { get; }
        
        /// <summary>
        /// Delete the File
        /// </summary>
        public void Delete();
    }


    public abstract class FileBase : IFile, IWithHashValue
    {
        protected FileBase(FileInfo fi)
        {
            this.fi = fi;
        }
        protected readonly FileInfo fi;

        public string FullName => fi.FullName;
        public string Name => fi.Name;
        public DirectoryInfo Directory => fi.Directory;

        public long Length => fi.Length;

        public void Delete()
        {
            fi.Delete();
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
        protected RelativeFileBase(DirectoryInfo root, FileInfo fi) : base(fi)
        {
            this.Root = root;
        }

        public string RelativeName => Path.GetRelativePath(Root.FullName, fi.FullName);
        public string RelativePath => Path.GetRelativePath(Root.FullName, fi.DirectoryName);
        public DirectoryInfo Root { get; }
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

        internal FileInfo BinaryFileInfo => new(fi.FullName.TrimEnd(Extension));

        internal IEnumerable<HashValue> ChunkHashes { get; set; }

        public override string ContentName => Name.TrimEnd(Extension);
    }

    public class BinaryFile : RelativeAriusFileBase, IAriusEntryWithHash, IChunkFile
    {
        public BinaryFile(DirectoryInfo root, FileInfo fi) : base(root, fi) { }

        internal IEnumerable<IChunkFile> Chunks { get; set; }

        public FileInfo PointerFileInfo => new FileInfo(fi.FullName + PointerFile.Extension);

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