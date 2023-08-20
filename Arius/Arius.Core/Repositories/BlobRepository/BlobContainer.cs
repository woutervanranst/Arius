using System.Net;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.Repositories.BlobRepository;

internal class BlobContainer
{
    internal const string STATE_DBS_FOLDER_NAME         = "states";
    internal const string CHUNK_LISTS_FOLDER_NAME       = "chunklists";
    internal const string CHUNKS_FOLDER_NAME            = "chunks";
    internal const string REHYDRATED_CHUNKS_FOLDER_NAME = "chunks-rehydrated";
    //internal const char BLOB_FOLDER_SEPARATOR_CHAR = '/';

    private readonly BlobContainerClient container;

    public BlobContainer(BlobContainerClient container)
    {
        this.container = container;

        States           = new (container, STATE_DBS_FOLDER_NAME);
        ChunkLists       = new (container, CHUNK_LISTS_FOLDER_NAME);
        Chunks           = new (container, CHUNKS_FOLDER_NAME);
        RehydratedChunks = new (container, REHYDRATED_CHUNKS_FOLDER_NAME);
    }

    /// <summary>
    /// Create the container if it does not exist
    /// </summary>
    /// <returns>True if it was created. False if it already existed.</returns>
    public async Task<bool> CreateIfNotExistsAsync()
    {
        var r = await container.CreateIfNotExistsAsync(PublicAccessType.None);

        if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
            return true;
        else
            return false;
    }

    public BlobContainerFolder<BlobEntry, Blob> States     { get; }
    public ChunkListBlobContainerFolder         ChunkLists { get; }
    public ChunkBlobContainerFolder             Chunks           { get; }
    public ChunkBlobContainerFolder             RehydratedChunks { get; }
}