using System;
using System.Collections.Generic;
using System.IO;
using Arius.Core.Extensions;

namespace Arius.Core.Models
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
            Root = root;
        }

        public string RelativeName => Path.GetRelativePath(Root.FullName, fi.FullName);
        public string RelativePath => Path.GetRelativePath(Root.FullName, fi.DirectoryName);
        public DirectoryInfo Root { get; }
        public override string ToString() => RelativeName;
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
        public static readonly string Extension = ".pointer.arius";
        public static string GetFullName(BinaryFile bf) => $"{bf.FullName}{PointerFile.Extension}";

        //public PointerFile(FileInfo fi) : this(fi.Directory, fi)
        //{
        //  DO NOT IMPLEMENT THIS, IT WILL CAUSE CONFUSION & BUGS & INVALID ARCHIVES
        //}

        /// <summary>
        /// Create a new PointerFile with the given root and read the Hash from the file
        /// </summary>
        public PointerFile(DirectoryInfo root, FileInfo fi) : base(root, fi)
        {
            Hash = new HashValue() { Value = File.ReadAllText(fi.FullName) };
        }

        /// <summary>
        /// Get the local BinaryFile for this pointer if it exists.
        /// If it does not exist, return null.
        /// </summary>
        /// <returns></returns>
        public BinaryFile GetBinaryFile()
        {
            var bfi = new FileInfo(fi.FullName.TrimEnd(Extension));

            if (!bfi.Exists)
                return null;

            return new BinaryFile(Root, bfi);
        }

        internal IEnumerable<HashValue> ChunkHashes { get; set; } //TODO Delete this

        public override string ContentName => Name.TrimEnd(Extension);
    }

    public class BinaryFile : RelativeAriusFileBase, IAriusEntryWithHash, IChunkFile
    {
        public BinaryFile(DirectoryInfo root, FileInfo fi) : base(root, fi) { }

        internal IEnumerable<IChunkFile> Chunks { get; set; } //TODO delete this

        /// <summary>
        /// Get the equivalent (in name and LastWriteTime) PointerFile if it exists.
        /// If it does not exist, return null.
        /// </summary>
        /// <returns></returns>
        public PointerFile GetPointerFile()
        {
            var pfi = new FileInfo(PointerFile.GetFullName(this));

            if (!pfi.Exists || pfi.LastWriteTimeUtc != File.GetLastWriteTimeUtc(FullName))
                return null;

            return new PointerFile(Root, pfi);
        }

        public override string ContentName => Name;
    }

    internal class ChunkFile : FileBase, IChunkFile
    {
        public static readonly string Extension = ".chunk.arius";

        public ChunkFile(FileInfo fi, HashValue hash) : base(fi)
        {
            Hash = hash;
        }
    }

    internal class EncryptedChunkFile : FileBase, IEncryptedFile, IChunkFile
    {
        public static readonly string Extension = ".7z.arius";

        public EncryptedChunkFile(FileInfo fi) : base(fi)
        {
            Hash = new HashValue { Value = fi.Name.TrimEnd(Extension) };
        }
        public EncryptedChunkFile(FileInfo fi, HashValue hash) : base(fi)
        {
            Hash = hash;
        }
    }
}