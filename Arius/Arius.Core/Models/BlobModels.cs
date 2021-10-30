using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace Arius.Core.Models;

internal abstract class BlobBase //: IWithHashValue
{
    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    public abstract string FullName { get; }

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    public string Name => FullName.Split(BlobFolderSeparatorChar).Last(); //TODO werkt dit met alle soorten repos?

    /// <summary>
    /// The Folder where this Blob resides
    /// </summary>
    public string Folder => FullName.Split(BlobFolderSeparatorChar).First(); //TODO quid if in the root?

    /// <summary>
    /// Length (in bytes) of the Blob
    /// </summary>
    public abstract long Length { get; }

    /// <summary>
    /// The Hash of this blob
    /// </summary>
    public abstract Hash Hash { get; }

    private const char BlobFolderSeparatorChar = '/';
}

internal class BinaryManifest : BlobBase //TODO THIS CLASS IS NOT USED A LOT?
{
    public BinaryManifest(BlobItem bi)
    {
        this.bi = bi;
    }
    protected readonly BlobItem bi;

    public override BinaryHash Hash => new(Name);
    public override string FullName => bi.Name;
    public override long Length => bi.Properties.ContentLength!.Value;
}



internal abstract class ChunkBlobBase : BlobBase, IChunk
{
    public static ChunkBlobItem GetChunkBlob(BlobItem bi)
    {
        return new ChunkBlobItem(bi);
    }
    public static ChunkBlobBaseClient GetChunkBlob(BlobBaseClient bc)
    {
        return new ChunkBlobBaseClient(bc);
    }


    public static readonly string Extension = ".ariuschunk.gz.aes";
    public bool Downloadable => AccessTier == AccessTier.Hot || AccessTier == AccessTier.Cool;
    public override ChunkHash Hash => new(Name.TrimEnd(Extension));

    public abstract AccessTier AccessTier { get; }
        
    public abstract Task<Stream> OpenReadAsync();
    public abstract Task<Stream> OpenWriteAsync();
        
    /// <summary>
    ///  The URI to this blob
    /// </summary>
    public abstract Uri Uri { get; }
}

internal class ChunkBlobItem : ChunkBlobBase
{
    internal ChunkBlobItem(BlobItem bi)
    {
        this.bi = bi;
    }
    private readonly BlobItem bi;


    public override long Length => bi.Properties.ContentLength!.Value;
    public override AccessTier AccessTier => bi.Properties.AccessTier!.Value;
    public override string FullName => bi.Name;

    public override Task<Stream> OpenReadAsync() => throw new NotImplementedException();
    public override Task<Stream> OpenWriteAsync() => throw new NotImplementedException();

    public override Uri Uri => throw new NotImplementedException();
}

internal class ChunkBlobBaseClient : ChunkBlobBase
{
    internal ChunkBlobBaseClient(BlobBaseClient bbc)
    {
        try
        {
            props = bbc.GetProperties().Value;
            this.bbc = bbc;
        }
        catch (Azure.RequestFailedException)
        {
            throw new ArgumentException($"Blob {bbc.Uri} not found. Either this is expected (no hydrated blob found) or the archive integrity is compromised?");
        }

    }
    private readonly BlobProperties props;
    private readonly BlobBaseClient bbc;

    public override long Length => props.ContentLength;

    public override AccessTier AccessTier => props.AccessTier switch
    {
        "Hot" => AccessTier.Hot,
        "Cool" => AccessTier.Cool,
        "Archive" => AccessTier.Archive,
        _ => throw new ArgumentException($"AccessTier not an expected value (is: {props.AccessTier}"),
    };

    public override string FullName => bbc.Name;

    public override Task<Stream> OpenReadAsync() => bbc.OpenReadAsync();
    public override Task<Stream> OpenWriteAsync() => throw new NotImplementedException();

    public override Uri Uri => bbc.Uri;
}