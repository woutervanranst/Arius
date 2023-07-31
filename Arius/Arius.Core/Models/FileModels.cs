using System;
using System.IO;
using System.Threading.Tasks;
using Arius.Core.Services;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arius.Core.Models;

internal abstract record FileBase
{
    private readonly FileInfoBase fib;

    protected FileBase(DirectoryInfo root, FileInfoBase fib, BinaryHash hash)
    {
        this.fib     = fib;

        Root         = root;
        RelativeName = Path.GetRelativePath(root.FullName, fib.FullName);
        BinaryHash   = hash;
    } 

    /// <summary>
    /// The root for this FileBase
    /// </summary>
    public DirectoryInfo Root { get; }

    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    public string FullName => fib.FullName;

    /// <summary>
    /// Relative File Name (with extention)
    /// </summary>
    public string RelativeName { get; }

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    public string Name => fib.Name;

    /// <summary>
    /// The hash of the binary
    /// </summary>
    public BinaryHash BinaryHash { get; }

    /// <summary>
    /// The creation time of the file in UTC
    /// </summary>
    public DateTime CreationTimeUtc => fib.CreationTimeUtc;

    /// <summary>
    /// The last time the file was written to in UTC
    /// </summary>
    public DateTime LastWriteTimeUtc => fib.LastWriteTimeUtc;


    /// <summary>
    /// Delete the File, if it exists
    /// </summary>
    public void Delete() => fib.Delete();

    public bool Exists() => fib.Exists;


    public sealed override string ToString() => $"'{RelativeName}' ({BinaryHash.ToShortString()})"; // marked sealed since records require re-overwriting https://stackoverflow.com/a/64094532/1582323
}

/// <inheritdoc/>
internal record PointerFile : FileBase
{
    public static readonly string Extension = ".pointer.arius";

    //private readonly Lazy<BinaryFile> binaryFile;

    public PointerFile(DirectoryInfo root, PointerFileInfo pfi, BinaryHash hash) : base(root, pfi, hash)
    {
        //binaryFile = new Lazy<BinaryFile>(new BinaryFile(rootPath, FileService.GetBinaryFileFullName(relativeName), hash));
    }

    //public BinaryFile BinaryFile => binaryFile.Value;
}

/// <inheritdoc cref="FileBase" />
internal record BinaryFile : FileBase, IChunk
{
    //private readonly Lazy<PointerFile> pointerFile;

    public BinaryFile(DirectoryInfo root, BinaryFileInfo bfi, BinaryHash hash) : base(root, bfi, hash)
    {
        //pointerFile = new Lazy<PointerFile>(new PointerFile(rootPath, FileService.GetPointerFileFullName(relativeName), hash));
    }


    /// <summary>
    /// The ChunkHash of this BinaryFile, should it be used as a Chunk
    /// </summary>
    public ChunkHash ChunkHash => BinaryHash;


    //public PointerFile PointerFile => pointerFile.Value;

    /// <summary>
    /// Length (in bytes) of the File
    /// </summary>
    public long Length => FileExtensions.Length(FullName);

    public Task<Stream> OpenReadAsync() => Task.FromResult((Stream)new FileStream(FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true));
    //public Task<Stream> OpenWriteAsync() => throw new NotImplementedException();
}