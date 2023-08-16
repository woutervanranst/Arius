using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Arius.Core.Repositories.BlobRepository;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public IAsyncEnumerable<ChunkBlobEntry> GetAllChunkBlobs() => container.Chunks.GetBlobEntriesAsync();

    public async Task<ChunkBlob> GetChunkBlobAsync(ChunkHash chunkHash) => await container.Chunks.GetBlobAsync(chunkHash);

    /// <summary>
    /// Get a hydrated chunk blob with the specified ChunkHash
    /// Returns null if no hydrated chunk exists.
    /// </summary>
    public async Task<ChunkBlob?> GetHydratedChunkBlobAsync(ChunkHash chunkHash)
    {
        var b = await container.Chunks.GetBlobAsync(chunkHash);

        if (!b.Exists)
            throw new InvalidOperationException($"Could not find Chunk {chunkHash.Value}");

        if (b.Hydrated)
            // the chunk in Chunks is hydrated
            return b;

        b = await container.RehydratedChunks.GetBlobAsync(chunkHash);

        if (b.Exists && b.Hydrated)
            // there is a hydrated chunk in the RehydratedChunks folder
            return b;

        // no hydrated chunk exists
        logger.LogDebug($"No hydrated chunk found for {chunkHash}");
        return null;
    }

    ///// <summary>
    ///// Get the RemoteEncryptedChunkBlobItem - either from permanent cold storage or from temporary rehydration storage
    ///// If the chunk does not exist, throws an InvalidOperationException
    ///// If requireHydrated is true and the chunk does not exist in cold storage, returns null
    ///// </summary>
    //public ChunkBlobBase GetChunkBlobByHash(ChunkHash chunkHash, bool requireHydrated)
    //{
    //    var blobName = GetChunkBlobName(ChunksFolderName, chunkHash);
    //    var cb1 = GetChunkBlobByName(blobName);

    //    if (cb1 is null)
    //        throw new InvalidOperationException($"Could not find Chunk {chunkHash.Value}");

    //    // if we don't need a hydrated chunk, return this one
    //    if (!requireHydrated)
    //        return cb1;

    //    // if we require a hydrated chunk, and this one is hydrated, return this one
    //    if (requireHydrated && cb1.Downloadable)
    //        return cb1;

    //    blobName = GetChunkBlobName(RehydratedChunksFolderName, chunkHash);
    //    var cb2 = GetChunkBlobByName(blobName);

    //    if (cb2 is null || !cb2.Downloadable)
    //    {
    //        // no hydrated chunk exists
    //        logger.LogDebug($"No hydrated chunk found for {chunkHash}");
    //        return null;
    //    }
    //    else
    //        return cb2;
    //}

    public async Task<long> GetChunkLengthAsync(ChunkHash chunkHash)
    {
        var b = await container.Chunks.GetBlobAsync(chunkHash); // TODO make DB-backed
        return b.Length;
    }

    public async Task<bool> ChunkExistsAsync(ChunkHash chunkHash)
    {
        var b = await container.Chunks.GetBlobAsync(chunkHash); // TODO make db backed
        return b.Exists;
    }

    public async Task HydrateChunkAsync(ChunkHash chunkHash)
    {
        logger.LogDebug($"Checking hydration for chunk {chunkHash}");

        var blobToHydrate = await GetChunkBlobAsync(chunkHash);

        if (blobToHydrate.AccessTier != AccessTier.Archive)
            throw new InvalidOperationException($"Calling Hydrate on a blob that is already hydrated ({blobToHydrate.Name})");

        var hydratedItem = await container.RehydratedChunks.GetBlobAsync(blobToHydrate.ChunkHash);

        if (!hydratedItem.Exists)
        {
            //Start hydration
            await hydratedItem.StartCopyFromUriAsync(
                blobToHydrate.Uri,
                new BlobCopyFromUriOptions { AccessTier = AccessTier.Cool, RehydratePriority = RehydratePriority.Standard });

            logger.LogInformation($"Hydration started for '{blobToHydrate.ChunkHash}'");
        }
        else
        {
            // Get hydration status
            // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

            if (hydratedItem.HydrationPending)
                logger.LogInformation($"Hydration pending for '{blobToHydrate.ChunkHash}'");
            else
                logger.LogInformation($"Hydration done for '{blobToHydrate.ChunkHash}'");
        }
    }

    public async Task DeleteHydratedChunksFolderAsync()
    {
        logger.LogInformation("Deleting temporary hydration folder");

        await foreach (var be in container.RehydratedChunks.GetBlobEntriesAsync())
            await container.RehydratedChunks.DeleteBlobAsync(be);
    }

    /// <summary>
    /// Upload a (plaintext) chunk to the repository after compressing and encrypting it
    /// </summary>
    /// <returns>Returns the length of the uploaded stream.</returns>
    internal async Task<long> UploadChunkAsync(IChunk chunk, AccessTier tier)
    {
        if (DateTime.UtcNow.Day > 16)
            throw new NotImplementedException("KAKPIS");

        logger.LogDebug($"Uploading Chunk '{chunk.ChunkHash}'...");

        var bbc = await container.Chunks.GetBlobAsync(chunk.ChunkHash);

    RestartUpload:

        try
        {
            // v12 with blockBlob.Upload: https://blog.matrixpost.net/accessing-azure-storage-account-blobs-from-c-applications/

            long length;
            await using (var ts = await bbc.OpenWriteAsync(overwrite: true, options: ThrowOnExistOptions)) //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
            {
                await using var ss = await chunk.OpenReadAsync();
                await CryptoService.CompressAndEncryptAsync(ss, ts, Options.Passphrase);
                length = ts.Position;
            }

            await bbc.SetContentTypeAsync(CryptoService.ContentType); //NOTE put this before SetAccessTier -- once Archived no more operations can happen on the blob

            // Set access tier per policy
            await bbc.SetAccessTierAsync(ChunkBlob.GetPolicyAccessTier(tier, length)); 

            logger.LogInformation($"Uploading Chunk '{chunk.ChunkHash}'... done");

            return length;
        }
        catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict /*409*/) //icw ThrowOnExistOptions. In case of hot/cool, throws a 409+BlobAlreadyExists. In case of archive, throws a 409+BlobArchived
        {
            // The blob already exists
            try
            {
                if (bbc.ContentType != CryptoService.ContentType || bbc.Length == 0)
                {
                    logger.LogWarning($"Corrupt chunk {chunk.ChunkHash}. Deleting and uploading again");
                    await bbc.DeleteAsync();

                    goto RestartUpload;
                }
                else
                {
                    // graceful handling if the chunk is already uploaded but it does not yet exist in the database
                    //throw new InvalidOperationException($"Chunk {chunk.Hash} with length {p.ContentLength} and contenttype {p.ContentType} already exists, but somehow we are uploading this again."); //this would be a multithreading issue
                    logger.LogWarning($"A valid Chunk '{chunk.ChunkHash}' already existsted, perhaps in a previous/crashed run?");

                    return bbc.Length;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Exception while reading properties of chunk {chunk.ChunkHash}");
                throw;
            }
        }
        catch (Exception e)
        {
            var e2 = new InvalidOperationException($"Error while uploading chunk {chunk.ChunkHash}. Deleting...", e);
            logger.LogError(e2); //TODO test this in a unit test
            await bbc.DeleteAsync();
            logger.LogDebug("Error while uploading chunk. Deleting potentially corrupt chunk... Success.");

            throw e2;
        }
    }
}
