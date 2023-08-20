using Arius.Core.Extensions;
using Arius.Core.Models;
using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    private const string JSON_GZIP_CONTENT_TYPE  = "application/json+gzip";

    internal async Task CreateChunkListAsync(BinaryHash bh, IList<ChunkHash> chunkHashes)
    {
        /* When writing to blob
         * Logging
         * Check if exists
         * Check tag
         * error handling around write / delete on fail
         * log
         */

        logger.LogDebug($"Creating ChunkList for '{bh}'...");

        if (chunkHashes.Count == 1)
            return; //do not create a ChunkList for only one ChunkHash

        var bbc = await container.ChunkLists.GetBlobAsync(bh.Value.BytesToHexString());

        RestartUpload:

        try
        {
            using (var ts = await bbc.OpenWriteAsync())
            {
                using var gzs = new GZipStream(ts, CompressionLevel.Optimal);
                await JsonSerializer.SerializeAsync(gzs, chunkHashes.Select(cf => cf.Value.BytesToHexString()));
            }

            await bbc.SetAccessTierAsync(AccessTier.Cold);
            await bbc.SetContentTypeAsync(JSON_GZIP_CONTENT_TYPE);

            logger.LogInformation($"Creating ChunkList for '{bh}'... done with {chunkHashes.Count} chunks");
        }
        catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict)
        {
            // The blob already exists
            try
            {
                if (bbc.ContentType != JSON_GZIP_CONTENT_TYPE || bbc.Length == 0)
                {
                    logger.LogWarning($"Corrupt ChunkList for {bh}. Deleting and uploading again");
                    await bbc.DeleteAsync();

                    goto RestartUpload;
                }
                else
                {
                    // gracful handling if the chunklist already exists:
                    //   throw new InvalidOperationException($"ChunkList for '{bh.ToShortString()}' already exists");
                    logger.LogWarning($"A valid ChunkList for '{bh}' already existed, perhaps in a previous/crashed run?");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Exception while reading properties of chunklist {bh}");
                throw;
            }
        }
        catch (Exception e)
        {
            var e2 = new InvalidOperationException($"Error when creating ChunkList {bh}. Deleting...", e);
            logger.LogError(e2);
            await bbc.DeleteAsync();
            logger.LogDebug("Succesfully deleted");

            throw e2;
        }
    }

    internal async IAsyncEnumerable<ChunkHash> GetChunkListAsync(BinaryHash bh)
    {
        logger.LogDebug($"Getting ChunkList for '{bh}'...");

        if ((await GetChunkEntryAsync(bh)).ChunkCount == 1)
            yield return bh;
        else
        {
            var b = await container.ChunkLists.GetBlobAsync(bh);

            if (!b.Exists)
                throw new InvalidOperationException($"ChunkList for '{bh}' does not exist");
            
            if (b.ContentType != JSON_GZIP_CONTENT_TYPE)
                throw new InvalidOperationException($"ChunkList '{bh}' does not have the '{JSON_GZIP_CONTENT_TYPE}' ContentType and is potentially corrupt");

            var i = 0;

            await using var ss  = await b.OpenReadAsync();
            await using var gzs = new GZipStream(ss, CompressionMode.Decompress);
            await foreach (var ch in JsonSerializer.DeserializeAsyncEnumerable<string>(gzs))
            {
                if (ch is null)
                    throw new InvalidOperationException("ChunkHash is null");

                i++;
                yield return new ChunkHash(ch.HexStringToBytes());
            }

            logger.LogInformation($"Getting chunks for binary '{bh}'... found {i} chunks");
        }
    }
}