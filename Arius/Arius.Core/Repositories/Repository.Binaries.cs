using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Commands.Archive;
using Arius.Core.Commands.Restore;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public BinaryRepository Binaries { get; init; }

    internal class BinaryRepository
    {
        private readonly ILogger<Repository>                                   logger;
        private readonly Repository                                            repo;
        private readonly BlobContainerClient                                   container;
        

        internal BinaryRepository(Repository parent, BlobContainerClient container)
        {
            this.logger = parent.logger;
            this.repo = parent;
            this.container = container;
        }


        // --- BINARY UPLOAD ------------------------------------------------

        

        // --- BINARY DOWNLOAD ------------------------------------------------

        /// <summary>
        /// Download the given Binary with the specified options.
        /// Start hydration for the chunks if required.
        /// Returns null if the Binary is not yet hydrated
        /// </summary>
        public async Task<bool> TryDownloadAsync(BinaryHash bh, BinaryFileInfo target, IRestoreCommandOptions options, bool rehydrateIfNeeded = true)
        {
            var chs = await GetChunkListAsync(bh);
            var chunks = chs.Select(ch => (ChunkHash: ch, ChunkBlob: repo.Chunks.GetChunkBlobByHash(ch, requireHydrated: true))).ToArray();

            var chunksToHydrate = chunks
                .Where(c => c.ChunkBlob is null)
                .Select(c => repo.Chunks.GetChunkBlobByHash(c.ChunkHash, requireHydrated: false));
            if (chunksToHydrate.Any())
            {
                chunksToHydrate = chunksToHydrate.ToArray();
                //At least one chunk is not hydrated so the Binary cannot be downloaded
                logger.LogInformation($"{chunksToHydrate.Count()} chunk(s) for '{bh.ToShortString()}' not hydrated. Cannot yet restore.");

                if (rehydrateIfNeeded)
                    foreach (var c in chunksToHydrate)
                        //hydrate this chunk
                        await repo.Chunks.HydrateAsync(c);

                return false;
            }
            else
            {
                //All chunks are hydrated  so we can restore the Binary
                logger.LogInformation($"Downloading Binary '{bh.ToShortString()}' from {chunks.Length} chunk(s)...");

                var p = await GetPropertiesAsync(bh);
                var stats = await new Stopwatch().GetSpeedAsync(p.ArchivedLength, async () =>
                {
                    await using var ts = target.OpenWrite(); // TODO add async 
                    
                    // Faster version but more code
                    //if (chunks.Length == 1)
                    //{
                    //    await using var cs = await chunks[0].ChunkBlob.OpenReadAsync();
                    //    await CryptoService.DecryptAndDecompressAsync(cs, ts, options.Passphrase);
                    //}
                    //else
                    //{
                    //    var x = new ConcurrentDictionary<ChunkHash, byte[]>();

                    //    var t0 = Task.Run(async () =>
                    //    {
                    //        await Parallel.ForEachAsync(chunks,
                    //            new ParallelOptions() { MaxDegreeOfParallelism = 20 },
                    //            async (c, ct) =>
                    //            {
                    //                await using var ms = new MemoryStream();
                    //                await using var cs = await c.ChunkBlob.OpenReadAsync();
                    //                await CryptoService.DecryptAndDecompressAsync(cs, ms, options.Passphrase);
                    //                if (!x.TryAdd(c.ChunkHash, ms.ToArray()))
                    //                    throw new InvalidOperationException();
                    //            });
                    //    });

                    //    var t1 = Task.Run(async () =>
                    //    {
                    //        foreach (var (ch, _) in chunks)
                    //        {
                    //            while (!x.ContainsKey(ch))
                    //                await Task.Yield();

                    //            if (!x.TryRemove(ch, out var buff))
                    //                throw new InvalidOperationException();

                    //            await ts.WriteAsync(buff);
                    //            //await x[ch].CopyToAsync(ts);
                    //        }
                    //    });

                    //    Task.WaitAll(t0, t1);
                    //}

                    foreach (var (_, cb) in chunks)
                    {
                        await using var cs = await cb.OpenReadAsync();
                        await CryptoService.DecryptAndDecompressAsync(cs, ts, options.Passphrase);
                    }
                });

                logger.LogInformation($"Downloading Binary '{bh.ToShortString()}' of {p.ArchivedLength.GetBytesReadable()} from {chunks.Length} chunk(s)... Completed in {stats.seconds}s ({stats.MBps} MBps / {stats.Mbps} Mbps)");

                return true;
            }
        }


        // --- BINARY PROPERTIES ------------------------------------------------

        internal async Task<BinaryProperties> CreatePropertiesAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount)
        {
            var bp = new BinaryProperties()
            {
                Hash = bf.BinaryHash,
                OriginalLength = bf.Length,
                ArchivedLength = archivedLength,
                IncrementalLength = incrementalLength,
                ChunkCount = chunkCount
            };

            await using var db = repo.GetAriusDbContext();
            await db.BinaryProperties.AddAsync(bp);
            await db.SaveChangesAsync();

            return bp;
        }

        public async Task<BinaryProperties> GetPropertiesAsync(BinaryHash bh)
        {
            try
            {
                await using var db = repo.GetAriusDbContext();
                return db.BinaryProperties.Single(bp => bp.Hash == bh);
            }
            catch (InvalidOperationException e) when (e.Message == "Sequence contains no elements")
            {
                throw new InvalidOperationException($"Could not find BinaryProperties for '{bh}'", e);
            }
        }

        public async Task<bool> ExistsAsync(BinaryHash bh)
        {
            await using var db = repo.GetAriusDbContext();
            return await db.BinaryProperties.AnyAsync(bm => bm.Hash == bh);
        }

        /// <summary>
        /// Get the count of (distinct) BinaryHashes
        /// </summary>
        /// <returns></returns>
        public async Task<int> CountAsync()
        {
            await using var db = repo.GetAriusDbContext();
            return await db.BinaryProperties.CountAsync();
            //return await db.PointerFileEntries
            //    .Select(pfe => pfe.BinaryHash)
            //    .Distinct()
            //    .CountAsync();
        }

        public async Task<long> TotalIncrementalLengthAsync()
        {
            await using var db = repo.GetAriusDbContext();
            return await db.BinaryProperties.SumAsync(bp => bp.IncrementalLength);
        }

        /// <summary>
        /// Get all the (distinct) BinaryHashes
        /// </summary>
        /// <returns></returns>
        public async Task<BinaryHash[]> GetAllBinaryHashesAsync()
        {
            await using var db = repo.GetAriusDbContext();
            return await db.BinaryProperties
                .Select(bp => bp.Hash)
                .ToArrayAsync();
            //return await db.PointerFileEntries
            //    .Select(pfe => pfe.BinaryHash)
            //    .Distinct()
            //    .ToArrayAsync();
        }

        // --- CHUNKLIST ------------------------------------------------

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

            if ((await GetPropertiesAsync(bh)).ChunkCount == 1)
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
        

        internal string GetChunkListBlobName(BinaryHash bh) => $"{ChunkListsFolderName}/{bh.Value}";

        private const string ChunkListsFolderName = "chunklists";

        private const string JsonGzipContentType = "application/json+gzip";
    }
}