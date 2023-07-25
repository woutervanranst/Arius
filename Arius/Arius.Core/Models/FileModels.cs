using System;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Core.Models;

internal abstract record FileBase
{
    protected FileBase(string fullName, string relativeName, BinaryHash hash)
    {
        FullName     = fullName;
        RelativeName = relativeName;
        Name         = Path.GetFileName(relativeName);
        BinaryHash   = hash;
    }

    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Relative File Name (with extention)
    /// </summary>
    public string RelativeName { get; }

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The hash of the binary
    /// </summary>
    public BinaryHash BinaryHash { get; }

    /// <summary>
    /// The creation time of the file in UTC
    /// </summary>
    public DateTime CreationTimeUtc => File.GetCreationTimeUtc(FullName);
    /// <summary>
    /// The last time the file was written to in UTC
    /// </summary>
    public DateTime LastWriteTimeUtc => File.GetLastWriteTimeUtc(FullName);


    /// <summary>
    /// Delete the File, if it exists
    /// </summary>
    public void Delete() => File.Delete(FullName);

    public bool Exists() => File.Exists(FullName);


    public override string ToString() => $"'{RelativeName}' ({BinaryHash.ToShortString()})";
}

/// <inheritdoc/>
internal record PointerFile : FileBase
{
    public static readonly string Extension = ".pointer.arius";

    //private readonly Lazy<BinaryFile> binaryFile;

    public PointerFile(string fullName, string relativeName, BinaryHash hash) : base(fullName, relativeName, hash)
    {
        //binaryFile = new Lazy<BinaryFile>(new BinaryFile(rootPath, FileService.GetBinaryFileFullName(relativeName), hash));
    }

    //public BinaryFile BinaryFile => binaryFile.Value;
}

/// <inheritdoc cref="FileBase" />
internal record BinaryFile : FileBase, IChunk
{
    //private readonly Lazy<PointerFile> pointerFile;

    public BinaryFile(string fullName, string relativeName, BinaryHash hash) : base(fullName, relativeName, hash)
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