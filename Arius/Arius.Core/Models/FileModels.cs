using System;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Core.Models;
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

/// <inheritdoc/>
internal abstract class FileBase : IFile
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
    public void Delete() => fi.Delete();

    public abstract Hash Hash { get; }
}

/// <inheritdoc/>
internal abstract class RelativeFileBase : FileBase
{
    protected RelativeFileBase(DirectoryInfo root, FileInfo fi) : base(fi)
    {
        Root = root;
    }

    public DirectoryInfo Root { get; }

    /// <summary>
    /// Relative File Name (with extention)
    /// </summary>
    public string RelativeName => Path.GetRelativePath(Root.FullName, fi.FullName);

    /// <summary>
    /// Relative File Path (directory)
    /// </summary>
    public string RelativePath => Path.GetRelativePath(Root.FullName, fi.DirectoryName);
    public override string ToString() => RelativeName;
}

/// <inheritdoc/>
internal class PointerFile : RelativeFileBase
{
    public static readonly string Extension = ".pointer.arius";

    //public PointerFile(FileInfo fi) : this(fi.Directory, fi)
    //{
    //  DO NOT IMPLEMENT THIS, IT WILL CAUSE CONFUSION & BUGS & INVALID ARCHIVES
    //}

    /// <summary>
    /// Create a new PointerFile with the given root and the given BinaryHash
    /// </summary>
    public PointerFile(DirectoryInfo root, FileInfo fi, BinaryHash binaryHash) : base(root, fi)
    {
        Hash = binaryHash;
    }

    public override BinaryHash Hash { get; }
}

/// <inheritdoc cref="RelativeFileBase" />
internal class BinaryFile : RelativeFileBase, IChunkFile, IChunk
{
    public BinaryFile(DirectoryInfo root, FileInfo fi, BinaryHash hash) : base(root, fi) 
    {
        Hash = hash;
    }

    public override BinaryHash Hash { get; }

    ChunkHash IChunk.Hash => new (Hash);

    public Task<Stream> OpenReadAsync() => Task.FromResult((Stream)base.fi.OpenRead());
    public Task<Stream> OpenWriteAsync() => throw new NotImplementedException();

    public override string ToString()
    {
        return $"'{Name}' ('{Hash.ToShortString()}')";
    }
}