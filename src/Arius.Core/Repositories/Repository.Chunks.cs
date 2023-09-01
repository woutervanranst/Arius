using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories.BlobRepository;
using Arius.Core.Services;
using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    /// <summary>
    /// Upload a (plaintext) chunk to the repository after compressing and encrypting it
    /// </summary>
    public async Task<ChunkEntry> UploadChunkAsync(IChunk chunk, AccessTier tier)
    {
        var cb = container.Chunks.GetBlob(chunk.ChunkHash);

    RestartUpload:

        try
        {
            // v12 with blockBlob.Upload: https://blog.matrixpost.net/accessing-azure-storage-account-blobs-from-c-applications/

            long originalLength, archivedLength;
            await using (var ts = await cb.OpenWriteAsync())
            {
                await using var ss = await chunk.OpenReadAsync();
                await CryptoService.CompressAndEncryptAsync(ss, ts, Options.Passphrase);
                originalLength = ss.Length;
                archivedLength = ts.Position; // ts.Length is not supported
            }

            // Set the Content Type
            await cb.SetContentTypeAsync(CryptoService.ContentType); //NOTE put this before SetAccessTier -- once Archived no more operations can happen on the blob

            // Set the Metadata
            await cb.SetOriginalLengthMetadata(originalLength);

            // Set access tier per policy - do this after all else because Archive blobs cannot be changed
            tier = ChunkBlob.GetPolicyAccessTier(tier, archivedLength);
            await cb.SetAccessTierAsync(tier);

            // Create the ChunkEntry
            var ce = await CreateChunkEntryAsync(chunk, originalLength, archivedLength, tier);

            logger.LogInformation($"Uploading Chunk '{chunk.ChunkHash}'... done");

            return ce;
        }
        catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict /*409*/) //icw ThrowOnExistOptions: throws this error when the blob already exists. In case of hot/cool, throws a 409+BlobAlreadyExists. In case of archive, throws a 409+BlobArchived
        {
            // The blob already exists
            // TODO should this error handling not be in the .OpenWriteAsync of the blob?
            try
            {
                if (await cb.GetContentType() != CryptoService.ContentType || await cb.GetArchivedLength() == 0)
                {
                    logger.LogWarning($"Corrupt chunk {chunk.ChunkHash}. Deleting and uploading again");
                    await cb.DeleteAsync();

                    goto RestartUpload;
                }
                else
                {
                    // graceful handling if the chunk is already uploaded but it does not yet exist in the database
                    //throw new InvalidOperationException($"Chunk {chunk.Hash} with length {p.ContentLength} and contenttype {p.ContentType} already exists, but somehow we are uploading this again."); //this would be a multithreading issue
                    logger.LogWarning($"A valid Chunk '{chunk.ChunkHash}' already existsted, perhaps in a previous/crashed run?");

                    try
                    {
                        return await GetChunkEntryAsync(chunk.ChunkHash); // TODO when would this path succeed / not result in an InvalidOperationException ??
                    }
                    catch (InvalidOperationException)
                    {
                        // The ChunkEntry did not exist in the database - recreate the chunkentry in the db
                        var originalLength = await cb.GetOriginalLengthMetadata() ?? 0;
                        var archivedLength = await cb.GetArchivedLength() ?? 0;
                        var accessTier     = await cb.GetAccessTierAsync();
                        
                        return await CreateChunkEntryAsync(chunk, originalLength, archivedLength , accessTier);
                    }
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
            await cb.DeleteAsync();
            logger.LogDebug("Error while uploading chunk. Deleting potentially corrupt chunk... Success.");

            throw e2;
        }
    }
    
    /// <summary>
    /// Get a hydrated chunk blob with the specified ChunkHash
    /// Returns null if no hydrated chunk exists.
    /// </summary>
    public async Task<ChunkBlob?> GetHydratedChunkBlobAsync(ChunkHash chunkHash)
    {
        var b = container.Chunks.GetBlob(chunkHash);

        if (!await b.ExistsAsync()) // we do the live exists call because we ll reuse the GetAccessTierAsync anyway
            throw new InvalidOperationException($"Could not find Chunk {chunkHash.Value}");

        if (await b.GetAccessTierAsync() != AccessTier.Archive)
            // the chunk in Chunks is hydrated
            return b;


        b = container.RehydratedChunks.GetBlob(chunkHash);

        if (await b.ExistsAsync() && await b.GetAccessTierAsync() != AccessTier.Archive)
            // there is a hydrated chunk in the RehydratedChunks folder
            return b;

        // no hydrated chunk exists
        logger.LogDebug($"No hydrated chunk found for {chunkHash}");
        return null;

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
    }

    private async Task HydrateChunkAsync(ChunkHash chunkHash)
    {
        logger.LogDebug($"Checking hydration for chunk {chunkHash}");

        var blobToHydrate = container.Chunks.GetBlob(chunkHash);

        if (await blobToHydrate.GetAccessTierAsync() != AccessTier.Archive)
            throw new InvalidOperationException($"Calling Hydrate on a blob that is already hydrated ({blobToHydrate.Name})");

        var hydratedItem = container.RehydratedChunks.GetBlob(chunkHash);

        if (!await hydratedItem.ExistsAsync())
        {
            //Start hydration
            await hydratedItem.StartCopyFromUriAsync(
                blobToHydrate.Uri,
                new BlobCopyFromUriOptions { AccessTier = AccessTier.Cold, RehydratePriority = RehydratePriority.Standard });

            logger.LogInformation($"Hydration started for '{blobToHydrate.ChunkHash}'");
        }
        else
        {
            // Get hydration status
            // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

            if (await hydratedItem.IsHydrationPendingAsync())
                logger.LogInformation($"Hydration pending for '{blobToHydrate.ChunkHash}'");
            else
                logger.LogInformation($"Hydration done for '{blobToHydrate.ChunkHash}'");
        }
    }

    /// <summary>
    /// Get the list of all hydrating chunks along with their status
    /// </summary>
    /// <returns></returns>
    public async IAsyncEnumerable<(ChunkHash ChunkHash, bool HydrationPending)> GetRehydratedChunksAsync()
    {
        await foreach ((string Name, ArchiveStatus? ArchiveStatus) b in container.RehydratedChunks.GetBlobsAsync()) 
            yield return (new ChunkHash(b.Name.HexStringToBytes()), Blob.IsHydrationPending(b.ArchiveStatus));
    }

    public async Task DeleteHydratedChunksFolderAsync()
    {
        logger.LogInformation("Deleting temporary hydration folder...");

        await container.RehydratedChunks.DeleteFolderAsync();

        logger.LogInformation("Deleting temporary hydration folder... Done");
    }

    public async Task UpdateAllChunksToTier(AccessTier targetAccessTier, int maxDegreeOfParallelism)
    {
        await using var db = GetStateDbContext();
        var  chunksToUpdate= db.ChunkEntries.Where(ce => ce.AccessTier != targetAccessTier);

        await Parallel.ForEachAsync(chunksToUpdate,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            async (ce, ct) =>
        {
            if (ce.AccessTier is null) // the AcessTier is null for the ChunkEntry of a chunked BinaryFile
                return;

            if (ce.AccessTier == AccessTier.Archive)
                return; // do not do mass hydration of archive tiers

            // Update the actual blob
            var ch = new ChunkHash(ce.Hash);
            await container.Chunks.GetBlob(ch).SetAccessTierAsync(targetAccessTier);
            
            // Update the DB
            ce.AccessTier = targetAccessTier;

            logger.LogInformation($"Chunk {ch} - set tier to '{targetAccessTier}'");
        });

        await db.SaveChangesAsync(); // TODO quid losing a lot of data?

    }
}
