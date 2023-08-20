using Arius.Core.Models;
using Azure.Storage.Blobs.Models;
using System.IO;

namespace Arius.Core.Repositories.BlobRepository;

internal record BlobEntry
{
    private readonly Blob.Properties properties;

    public BlobEntry(BlobItem item)
    {
        FullName   = item.Name;
        properties = new Blob.Properties(item.Properties);
    }

    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    public string Name => Path.GetFileName(FullName);

    /// <summary>
    /// The Folder where this Blob resides. If the folder is in the root, returns an empty string.
    /// </summary>
    public string Folder => Path.GetDirectoryName(FullName) ?? string.Empty;

    public long?       Length         => properties.Length;
    public long?       OriginalLength => properties.OriginalLength;
    public string?     ContentType    => properties.ContentType;
    public AccessTier? AccessTier     => properties.AccessTier;
    public bool        Exists         => properties.Exists;
    public string?     ArchiveStatus  => properties.ArchiveStatus;


    // METHODS
    public override string ToString() => FullName;
}

internal record ChunkBlobEntry : BlobEntry
{
    public ChunkBlobEntry(BlobItem item) : base(item)
    {
        ChunkHash = new ChunkHash(Name.HexStringToBytes());
    }

    public ChunkHash ChunkHash { get; }
}

internal record ChunkListBlobEntry : BlobEntry
{
    public ChunkListBlobEntry(BlobItem item) : base(item)
    {
        BinaryHash = new BinaryHash(Name.HexStringToBytes());
    }

    public BinaryHash BinaryHash { get; }
}