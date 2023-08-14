using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    private const string ChunkListsFolderName = "chunklists";
    private const string JsonGzipContentType  = "application/json+gzip";

    internal static string GetChunkListBlobName(BinaryHash bh) => $"{ChunkListsFolderName}/{bh.Value}";

    internal async Task CreateChunkListAsync(BinaryHash bh, ChunkHash[] chunkHashes)
    {
        /* When writing to blob
         * Logging
         * Check if exists
         * Check tag
         * error handling around write / delete on fail
         * log
         */

        logger.LogDebug($"Creating ChunkList for '{bh.ToShortString()}'...");

        if (chunkHashes.Length == 1)
            return; //do not create a ChunkList for only one ChunkHash

        var bbc = container.GetBlockBlobClient(GetChunkListBlobName(bh));

        RestartUpload:

        try
        {
            using (var ts = await bbc.OpenWriteAsync(overwrite: true, options: ThrowOnExistOptions)) //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
            {
                using var gzs = new GZipStream(ts, CompressionLevel.Optimal);
                await JsonSerializer.SerializeAsync(gzs, chunkHashes.Select(cf => cf.Value));
            }

            await bbc.SetAccessTierAsync(AccessTier.Cool);
            await bbc.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = JsonGzipContentType });

            logger.LogInformation($"Creating ChunkList for '{bh.ToShortString()}'... done with {chunkHashes.Length} chunks");
        }
        catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict)
        {
            // The blob already exists
            try
            {
                var p = (await bbc.GetPropertiesAsync()).Value;
                if (p.ContentType != JsonGzipContentType || p.ContentLength == 0)
                {
                    logger.LogWarning($"Corrupt ChunkList for {bh}. Deleting and uploading again");
                    await bbc.DeleteAsync();

                    goto RestartUpload;
                }
                else
                {
                    // gracful handling if the chunklist already exists
                    //throw new InvalidOperationException($"ChunkList for '{bh.ToShortString()}' already exists");
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
            var e2 = new InvalidOperationException($"Error when creating ChunkList {bh.ToShortString()}. Deleting...", e);
            logger.LogError(e2);
            await bbc.DeleteAsync();
            logger.LogDebug("Succesfully deleted");

            throw e2;
        }
    }

    internal async Task<ChunkHash[]> GetChunkListAsync(BinaryHash bh)
    {
        logger.LogDebug($"Getting ChunkList for '{bh.ToShortString()}'...");

        if ((await GetBinaryPropertiesAsync(bh)).ChunkCount == 1)
            return ((ChunkHash)bh).AsArray();

        var chs = default(ChunkHash[]);

        try
        {
            var bbc = container.GetBlockBlobClient(GetChunkListBlobName(bh));

            if ((await bbc.GetPropertiesAsync()).Value.ContentType != JsonGzipContentType)
                throw new InvalidOperationException($"ChunkList '{bh}' does not have the '{JsonGzipContentType}' ContentType and is potentially corrupt");

            using (var ss = await bbc.OpenReadAsync())
            {
                using var gzs = new GZipStream(ss, CompressionMode.Decompress);
                chs = (await JsonSerializer.DeserializeAsync<IEnumerable<string>>(gzs))
                    !.Select(chv => new ChunkHash(chv))
                    .ToArray();

                logger.LogInformation($"Getting chunks for binary '{bh.ToShortString()}'... found {chs.Length} chunk(s)");

                return chs;
            }
        }
        catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
        {
            throw new InvalidOperationException($"ChunkList for '{bh.ToShortString()}' does not exist");
        }
    }
}