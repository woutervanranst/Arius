using System;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Core.Models;

internal abstract class FileBase
{
    protected readonly FileInfo fi;

    protected FileBase(DirectoryInfo root, FileInfo fi)
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

    public abstract Hash BinaryHash { get; }

    public override string ToString() => $"'{RelativeName}' ({BinaryHash.ToShortString()})";
}

/// <inheritdoc/>
internal class PointerFile : FileBase
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
        BinaryHash = binaryHash;
    }

    public override BinaryHash BinaryHash { get; }
}

/// <inheritdoc cref="FileBase" />
internal class BinaryFile : FileBase, IChunk
{
    public BinaryFile(DirectoryInfo root, FileInfo fi, BinaryHash hash) : base(root, fi) 
    {
        BinaryHash = hash;
    }

    public override BinaryHash BinaryHash { get; }

    public ChunkHash ChunkHash => BinaryHash;

    /// <summary>
    /// Length (in bytes) of the File
    /// </summary>
    public long Length => fi.Length;

    public Task<Stream> OpenReadAsync() => Task.FromResult((Stream)new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true));
    //public Task<Stream> OpenWriteAsync() => throw new NotImplementedException();
}