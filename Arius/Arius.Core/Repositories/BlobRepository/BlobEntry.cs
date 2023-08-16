using System.Linq;
using Arius.Core.Models;
using Azure.Storage.Blobs.Models;

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
    public string Name => FullName.Split(BlobContainer.BLOB_FOLDER_SEPARATOR_CHAR).Last(); //TODO werkt dit met alle soorten repos?

    /// <summary>
    /// The Folder where this Blob resides
    /// </summary>
    public string Folder => FullName.Split(BlobContainer.BLOB_FOLDER_SEPARATOR_CHAR).First(); //TODO quid if in the root?

    public long?       Length        => properties.Length;
    public string?     ContentType   => properties.ContentType;
    public AccessTier? AccessTier    => properties.AccessTier;
    public bool        Exists        => properties.Exists;
    public string?     ArchiveStatus => properties.ArchiveStatus;


    // METHODS
    public override string ToString() => FullName;
}

internal record ChunkBlobEntry : BlobEntry
{
    public ChunkBlobEntry(BlobItem item) : base(item)
    {
    }

    public ChunkHash ChunkHash => new(Name);
}