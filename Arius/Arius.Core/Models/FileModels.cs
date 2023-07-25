using System;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Core.Models;

internal abstract class RelativeFileBase
{
    protected readonly FileInfo fi;

    protected RelativeFileBase(DirectoryInfo root, FileInfo fi)
    {
        this.fi = fi;
        Root = root;
    }

    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    public string FullName => fi.FullName;

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    public string Name => fi.Name;

    /// <summary>
    /// The Directory where this File resides
    /// </summary>
    public DirectoryInfo Directory => fi.Directory;

    /// <summary>
    /// Length (in bytes) of the File
    /// </summary>
    public long Length => fi.Length;

    /// <summary>
    /// Delete the File
    /// </summary>
    public void Delete() => fi.Delete();

    public DirectoryInfo Root { get; }

    /// <summary>
    /// Relative File Name (with extention)
    /// </summary>
    public string RelativeName => Path.GetRelativePath(Root.FullName, fi.FullName);

    /// <summary>
    /// Relative File Path (directory)
    /// </summary>
    public string RelativePath => Path.GetRelativePath(Root.FullName, fi.DirectoryName);

    public abstract Hash Hash { get; }

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
internal class BinaryFile : RelativeFileBase, IChunk
{
    public BinaryFile(DirectoryInfo root, FileInfo fi, BinaryHash hash) : base(root, fi) 
    {
        Hash = hash;
    }

    public override BinaryHash Hash { get; }

    ChunkHash IChunk.Hash => new (Hash);

    public Task<Stream> OpenReadAsync() => Task.FromResult((Stream)base.fi.OpenRead());
    //public Task<Stream> OpenWriteAsync() => throw new NotImplementedException();

    public override string ToString()
    {
        return $"'{Name}' ('{Hash.ToShortString()}')";
    }
}