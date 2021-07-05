using System;
using System.Collections.Generic;
using System.IO;
using Arius.Core.Extensions;
using Arius.Core.Repositories;

namespace Arius.Core.Models
{
    //public interface IAriusEntry
    //{
    //    /// <summary>
    //    /// Path relative to the root
    //    /// </summary>
    //    public string RelativePath { get; }

    //    /// <summary>
    //    /// Name (without path but with Extension) of the (equivalent) BinaryFile (eg. 'myFile.bmp')
    //    /// </summary>
    //    public string ContentName { get; }
    //}

    //internal interface IWithHashValue
    //{
    //    public Hash Hash { get; }
    //}

    /// <inheritdoc/>
    //internal interface IAriusEntryWithHash : IAriusEntry //, IWithHashValue
    //{
    //}


    internal interface IFile
    {
        /// <summary>
        /// Full Name (with path and extension)
        /// </summary>
        public string FullName { get; }

        /// <summary>
        /// Name (with extension, without path)
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The Directory where this File resides
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


    internal abstract class FileBase : IFile //, IWithHashValue
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

        protected Hash INTERNALHASH
        {
            get => hash;
            set
            {
                if (hash is not null)
                    throw new InvalidOperationException("CAN ONLY BE SET ONCE");

                hash = value;
            }
        }
        private Hash hash;
    }

    internal abstract class RelativeFileBase : FileBase
    {
        protected RelativeFileBase(DirectoryInfo root, FileInfo fi) : base(fi)
        {
            Root = root;
        }

        public string RelativeName => Path.GetRelativePath(Root.FullName, fi.FullName);
        public string RelativePath => Path.GetRelativePath(Root.FullName, fi.DirectoryName);
        public DirectoryInfo Root { get; }
        public override string ToString() => RelativeName;
    }

    //public abstract class RelativeAriusFileBase : RelativeFileBase, IAriusEntryWithHash
    //{
    //    protected RelativeAriusFileBase(DirectoryInfo root, FileInfo fi) : base(root, fi)
    //    {
    //    }

    //    public abstract string ContentName { get; }
    //}



    internal interface IChunkFile : IFile //, IWithHashValue
    {
        ChunkHash Hash { get; }
    }

    internal interface IEncryptedFile : IFile
    {
    }


    internal class PointerFile : RelativeFileBase //, IAriusEntryWithHash
    {
        public static readonly string Extension = ".pointer.arius";

        //public PointerFile(FileInfo fi) : this(fi.Directory, fi)
        //{
        //  DO NOT IMPLEMENT THIS, IT WILL CAUSE CONFUSION & BUGS & INVALID ARCHIVES
        //}

        /// <summary>
        /// Create a new PointerFile with the given root and read the Hash from the file
        /// </summary>
        public PointerFile(DirectoryInfo root, FileInfo fi) : base(root, fi)
        {
            INTERNALHASH = new ManifestHash { Value = File.ReadAllText(fi.FullName) };
        }

        internal IEnumerable<ChunkHash> ChunkHashes { get; set; } //TODO Delete this

        //public override string ContentName => Name.TrimEnd(Extension);

        public ManifestHash Hash { get => (ManifestHash)INTERNALHASH; set => INTERNALHASH = value; }
    }

    internal class BinaryFile : RelativeFileBase /*, IAriusEntryWithHash*/, IChunkFile
    {
        public BinaryFile(DirectoryInfo root, FileInfo fi) : base(root, fi) { }
        
        //internal IEnumerable<IChunkFile> Chunks { get; set; } //TODO delete this

        //public override string ContentName => Name;

        public ManifestHash Hash { get => (ManifestHash)base.INTERNALHASH; set => base.INTERNALHASH = value; }

        ChunkHash IChunkFile.Hash => new() { Value = base.INTERNALHASH.Value };
    }


    internal class ChunkFile : FileBase, IChunkFile
    {
        public static readonly string Extension = ".chunk.arius";

        public ChunkFile(FileInfo fi, ChunkHash hash) : base(fi)
        {
            INTERNALHASH = hash;
        }

        public ChunkHash Hash { get => (ChunkHash)base.INTERNALHASH; set => base.INTERNALHASH = value; }

        ChunkHash IChunkFile.Hash => (ChunkHash)base.INTERNALHASH;
    }

    internal class EncryptedChunkFile : FileBase, IEncryptedFile, IChunkFile
    {
        public static readonly string Extension = ".7z.arius";

        /// <summary>
        /// Create a new EncryptedChunkFile with the hash derived from the name
        /// </summary>
        public EncryptedChunkFile(FileInfo fi) : base(fi)
        {
            INTERNALHASH = new ChunkHash { Value = fi.Name.TrimEnd(Extension) };
        }

        /// <summary>
        /// Create a new EncryptedChunkFile with the given hash
        /// </summary>
        public EncryptedChunkFile(FileInfo fi, ChunkHash hash) : base(fi)
        {
            INTERNALHASH = hash;
        }

        public ChunkHash Hash { get => (ChunkHash)base.INTERNALHASH; set => base.INTERNALHASH = value; }
        ChunkHash IChunkFile.Hash => (ChunkHash)base.INTERNALHASH;
    }
}