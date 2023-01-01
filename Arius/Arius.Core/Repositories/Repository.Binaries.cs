﻿using Arius.Core.Models;
using Azure;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Commands.Archive;
using Arius.Core.Extensions;
using Arius.Core.Services.Chunkers;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Blobs.Specialized;
using System.IO.Compression;
using System.Net;
using Arius.Core.Commands.Restore;
using Arius.Core.Services;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public BinaryRepository Binaries { get; init; }

    internal class BinaryRepository
    {
        internal BinaryRepository(ILogger<BinaryRepository> logger,
            Repository parent,
            BlobContainerClient container,
            Chunker chunker)
        {
            this.logger = logger;
            this.repo = parent;
            this.chunker = chunker;
            this.container = container;
        }

        private readonly ILogger<BinaryRepository> logger;
        private readonly Repository repo;
        private readonly Chunker chunker;
        private readonly BlobContainerClient container;
        private readonly ConcurrentDictionary<ChunkHash, TaskCompletionSource> uploadingChunks = new();

        // --- BINARY UPLOAD ------------------------------------------------

        /// <summary>
        /// Upload the given BinaryFile with the specified options
        /// </summary>
        public async Task<BinaryProperties> UploadAsync(BinaryFile bf, IArchiveCommandOptions options)
        {
            logger.LogInformation($"Uploading Binary '{bf.Name}' ('{bf.Hash.ToShortString()}') of {bf.Length.GetBytesReadable()}...");

            // Upload the Binary
            var (MBps, Mbps, seconds, chs, totalLength, incrementalLength) = await new Stopwatch().GetSpeedAsync(bf.Length, async () =>
            {
                if (options.Dedup)
                    return await UploadChunkedBinaryAsync(bf, options);
                else
                    return await UploadBinaryAsSingleChunkAsync(bf, options);
            });

            logger.LogInformation($"Uploading Binary '{bf.Name}' ('{bf.Hash.ToShortString()}') of {bf.Length.GetBytesReadable()}... Completed in {seconds}s ({MBps} MBps / {Mbps} Mbps)");

            // Create the ChunkList
            await CreateChunkHashListAsync(bf.Hash, chs);

            // Create the BinaryMetadata
            return await CreatePropertiesAsync(bf, totalLength, incrementalLength, chs.Length);

        }

        /// <summary>
        /// Chunk the BinaryFile then upload all the chunks in parallel
        /// </summary>
        /// <param name="bf"></param>
        /// <returns></returns>
        private async Task<(ChunkHash[], long totalLength, long incrementalLength)> UploadChunkedBinaryAsync(BinaryFile bf, IArchiveCommandOptions options)
        {
            var chunksToUpload = Channel.CreateBounded<IChunk>(new BoundedChannelOptions(options.TransferChunked_ChunkBufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = true, SingleReader = false }); //limit the capacity of the collection -- backpressure
            var chs = new List<ChunkHash>(); //ChunkHashes for this BinaryFile
            var totalLength = 0L;
            var incrementalLength = 0L;

            // Design choice: deliberately splitting the chunking section (which cannot be parallelized since we need the chunks in order) and the upload section (which can be paralellelized)
            var chunkTask = Task.Run(async () =>
            {
                await using var binaryFileStream = await bf.OpenReadAsync();

                var (MBps, _, seconds) = await new Stopwatch().GetSpeedAsync(bf.Length, async () =>
                {
                    foreach (var chunk in chunker.Chunk(binaryFileStream))
                    {
                        await chunksToUpload.Writer.WriteAsync(chunk);
                        chs.Add(chunk.Hash);
                    }
                });

                logger.LogInformation($"Completed chunking of {bf.Name} in {seconds}s at {MBps} MBps");

                chunksToUpload.Writer.Complete();
            });

            /* Design choice: deliberately keeping the chunk upload IN this block (not in a separate top level block like in v1) 
             * 1. to effectively limit the number of concurrent files 'in flight' 
             * 2. to avoid the risk on slow upload connections of filling up the memory entirely*
             * 3. this code has a nice 'await for binary upload completed' semantics contained within this method - splitting it over multiple blocks would smear it out, as in v1
             * 4. with a centralized pipe, setting the degree of concurrency is not trivial since, for chunks (~64 KB), it is higher than for full binary files (we dont want to be uploading 128 2GB files in parallel)
             */

            int degreeOfParallelism = 0;

            await Parallel.ForEachAsync(chunksToUpload.Reader.ReadAllAsync(),
                new ParallelOptions { MaxDegreeOfParallelism = options.TransferChunked_ParallelChunkTransfers

                },
                async (chunk, cancellationToken) =>
                {
                    var i = Interlocked.Add(ref degreeOfParallelism, 1); // store in variable that is local since threads will ramp up and set the dop value to much higher before the next line is hit
                    logger.LogDebug($"Starting chunk upload '{chunk.Hash.ToShortString()}' for {bf.Name}. Current parallelism {i}, remaining queue depth: {chunksToUpload.Reader.Count}");


                    if (await repo.Chunks.ExistsAsync(chunk.Hash)) //TODO: while the chance is infinitesimally low, implement like the manifests to avoid that a duplicate chunk will start a upload right after each other
                    {
                        // 1 Exists remote
                        logger.LogDebug($"Chunk with hash '{chunk.Hash.ToShortString()}' already exists. No need to upload.");

                        var length = repo.Chunks.GetChunkBlobByHash(chunk.Hash, false).Length;
                        Interlocked.Add(ref totalLength, length);
                        Interlocked.Add(ref incrementalLength, 0);
                    }
                    else
                    {
                        var toUpload = uploadingChunks.TryAdd(chunk.Hash, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                        if (toUpload)
                        {
                            // 2 Does not yet exist remote and not yet being created --> upload
                            logger.LogDebug($"Chunk with hash '{chunk.Hash.ToShortString()}' does not exist remotely. To upload.");

                            var length = await repo.Chunks.UploadAsync(chunk, options.Tier);
                            Interlocked.Add(ref totalLength, length);
                            Interlocked.Add(ref incrementalLength, length);

                            uploadingChunks[chunk.Hash].SetResult();
                        }
                        else
                        {
                            // 3 Does not exist remote but is being created by another thread
                            logger.LogDebug($"Chunk with hash '{chunk.Hash.ToShortString()}' does not exist remotely but is already being uploaded. Wait for its creation.");

                            await uploadingChunks[chunk.Hash].Task;

                            var length = repo.Chunks.GetChunkBlobByHash(chunk.Hash, false).Length;
                            Interlocked.Add(ref totalLength, length);
                            Interlocked.Add(ref incrementalLength, 0);

                            //TODO Write unit test for this path
                        }
                    }

                    Interlocked.Add(ref degreeOfParallelism, -1);
                });
            await chunkTask; //this task will always be compete at this point

            return (chs.ToArray(), totalLength, incrementalLength);
        }

        /// <summary>
        /// Upload one single BinaryFile
        /// </summary>
        /// <param name="bf"></param>
        /// <returns></returns>
        private async Task<(ChunkHash[], long totalLength, long incrementalLength)> UploadBinaryAsSingleChunkAsync(BinaryFile bf, IArchiveCommandOptions options)
        {
            var length = await repo.Chunks.UploadAsync(bf, options.Tier);

            return (((IChunk)bf).Hash.SingleToArray(), length, length);
        }

        // --- BINARY DOWNLOAD ------------------------------------------------

        /// <summary>
        /// Download the given Binary with the specified options.
        /// Start hydration for the chunks if required.
        /// Returns null if the Binary is not yet hydrated
        /// </summary>
        public async Task<bool> TryDownloadAsync(BinaryHash bh, FileInfo target, IRestoreCommandOptions options, bool rehydrateIfNeeded = true)
        {
            var chs = await GetChunkHashesAsync(bh);
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
                    await using var ts = target.OpenWrite();
                    
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

        private async Task<BinaryProperties> CreatePropertiesAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount)
        {
            var bp = new BinaryProperties()
            {
                Hash = bf.Hash,
                OriginalLength = bf.Length,
                ArchivedLength = archivedLength,
                IncrementalLength = incrementalLength,
                ChunkCount = chunkCount
            };

            await using var db = await repo.States.GetCurrentStateDbContextAsync();
            await db.BinaryProperties.AddAsync(bp);
            await db.SaveChangesAsync();

            return bp;
        }

        public async Task<BinaryProperties> GetPropertiesAsync(BinaryHash bh)
        {
            try
            {
                await using var db = await repo.States.GetCurrentStateDbContextAsync();
                return db.BinaryProperties.Single(bp => bp.Hash == bh);
            }
            catch (InvalidOperationException e) when (e.Message == "Sequence contains no elements")
            {
                throw new InvalidOperationException($"Could not find BinaryProperties for '{bh}'", e);
            }
        }

        public async Task<bool> ExistsAsync(BinaryHash bh)
        {
            await using var db = await repo.States.GetCurrentStateDbContextAsync();
            return await db.BinaryProperties.AnyAsync(bm => bm.Hash == bh);
        }

        /// <summary>
        /// Get the count of (distinct) BinaryHashes
        /// </summary>
        /// <returns></returns>
        public async Task<int> CountAsync()
        {
            await using var db = await repo.States.GetCurrentStateDbContextAsync();
            return await db.BinaryProperties.CountAsync();
            //return await db.PointerFileEntries
            //    .Select(pfe => pfe.BinaryHash)
            //    .Distinct()
            //    .CountAsync();
        }

        public async Task<long> TotalIncrementalLengthAsync()
        {
            await using var db = await repo.States.GetCurrentStateDbContextAsync();
            return await db.BinaryProperties.SumAsync(bp => bp.IncrementalLength);
        }

        /// <summary>
        /// Get all the (distinct) BinaryHashes
        /// </summary>
        /// <returns></returns>
        public async Task<BinaryHash[]> GetAllBinaryHashesAsync()
        {
            await using var db = await repo.States.GetCurrentStateDbContextAsync();
            return await db.BinaryProperties
                .Select(bp => bp.Hash)
                .ToArrayAsync();
            //return await db.PointerFileEntries
            //    .Select(pfe => pfe.BinaryHash)
            //    .Distinct()
            //    .ToArrayAsync();
        }

        // --- CHUNKLIST ------------------------------------------------

        internal async Task CreateChunkHashListAsync(BinaryHash bh, ChunkHash[] chunkHashes)
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

            BlockBlobClient bbc = container.GetBlockBlobClient(GetChunkListBlobName(bh));

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

        internal async Task<ChunkHash[]> GetChunkHashesAsync(BinaryHash bh)
        {
            logger.LogDebug($"Getting ChunkList for '{bh.ToShortString()}'...");

            if ((await GetPropertiesAsync(bh)).ChunkCount == 1)
                return new ChunkHash(bh).SingleToArray();

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