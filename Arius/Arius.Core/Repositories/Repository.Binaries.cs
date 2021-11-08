using Arius.Core.Models;
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

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public BinaryRepository Binaries { get; init; }
    internal class BinaryRepository
    {
        internal BinaryRepository(ILogger<BinaryRepository> logger, 
            Repository parent,
            Chunker chunker,
            BlobContainerClient container)
        {
            this.logger = logger;
            this.parent = parent;
            this.chunker = chunker;
            this.container = container;
        }

        private readonly ILogger<BinaryRepository> logger;
        private readonly Repository parent;
        private readonly Chunker chunker;
        private readonly BlobContainerClient container;
        private readonly ConcurrentDictionary<ChunkHash, TaskCompletionSource> uploadingChunks = new();

        // --- BINARY ------------------------

        /// <summary>
        /// Upload the given BinaryFile with the specified options
        /// </summary>
        /// <param name="bf"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public async Task UploadAsync(BinaryFile bf, ArchiveCommandOptions options)
        {
            logger.LogInformation($"Uploading {bf.Length.GetBytesReadable()} of '{bf.Name}' ('{bf.Hash.ToShortString()}')...");

            // Upload the Binary
            var (MBps, Mbps, seconds, chs, totalLength, incrementalLength) = await new Stopwatch().GetSpeedAsync(bf.Length, async () =>
            {
                if (options.Dedup)
                    return await UploadChunkedBinaryAsync(bf, options);
                else
                    return await UploadBinaryAsSingleChunkAsync(bf, options);
            });

            logger.LogInformation($"Uploading {bf.Length.GetBytesReadable()} of {bf}... Completed in {seconds}s ({MBps} MBps / {Mbps} Mbps)");

            // Create the ChunkList
            await CreateChunkHashListAsync(bf.Hash, chs);

            // Create the BinaryMetadata
            await CreatePropertiesAsync(bf, totalLength, incrementalLength, chs.Length);

        }

        /// <summary>
        /// Chunk the BinaryFile then upload all the chunks in parallel
        /// </summary>
        /// <param name="bf"></param>
        /// <returns></returns>
        private async Task<(ChunkHash[], long totalLength, long incrementalLength)> UploadChunkedBinaryAsync(BinaryFile bf, ArchiveCommandOptions options)
        {
            var chunksToUpload = Channel.CreateBounded<IChunk>(new BoundedChannelOptions(options.TransferChunked_ChunkBufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = true, SingleReader = false }); //limit the capacity of the collection -- backpressure
            var chs = new List<ChunkHash>(); //ChunkHashes for this BinaryFile
            var totalLength = 0L;
            var incrementalLength = 0L;

            // Design choice: deliberately splitting the chunking section (which cannot be parallelized since we need the chunks in order) and the upload section (which can be paralellelized)
            var t = Task.Run(async () =>
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


                    if (await parent.Chunks.ExistsAsync(chunk.Hash)) //TODO: while the chance is infinitesimally low, implement like the manifests to avoid that a duplicate chunk will start a upload right after each other
                    {
                        // 1 Exists remote
                        logger.LogDebug($"Chunk with hash '{chunk.Hash.ToShortString()}' already exists. No need to upload.");

                        var length = parent.Chunks.GetChunkBlobByHash(chunk.Hash, false).Length;
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

                            var length = await parent.Chunks.UploadAsync(chunk, options.Tier);
                            Interlocked.Add(ref totalLength, length);
                            Interlocked.Add(ref incrementalLength, length);

                            uploadingChunks[chunk.Hash].SetResult();
                        }
                        else
                        {
                            // 3 Does not exist remote but is being created by another thread
                            logger.LogDebug($"Chunk with hash '{chunk.Hash.ToShortString()}' does not exist remotely but is already being uploaded. Wait for its creation.");

                            await uploadingChunks[chunk.Hash].Task;

                            var length = parent.Chunks.GetChunkBlobByHash(chunk.Hash, false).Length;
                            Interlocked.Add(ref totalLength, length);
                            Interlocked.Add(ref incrementalLength, 0);

                        //TODO TES THIS PATH
                        }
                    }

                    Interlocked.Add(ref degreeOfParallelism, -1);
                });

            return (chs.ToArray(), totalLength, incrementalLength);
        }

        /// <summary>
        /// Upload one single BinaryFile
        /// </summary>
        /// <param name="bf"></param>
        /// <returns></returns>
        private async Task<(ChunkHash[], long totalLength, long incrementalLength)> UploadBinaryAsSingleChunkAsync(BinaryFile bf, ArchiveCommandOptions options)
        {
            var length = await parent.Chunks.UploadAsync(bf, options.Tier);

            return (((IChunk)bf).Hash.SingleToArray(), length, length);
        }


        private async Task CreatePropertiesAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount)
        {
            var bm = new BinaryProperties()
            {
                Hash = bf.Hash,
                OriginalLength = bf.Length,
                ArchivedLength = archivedLength,
                IncrementalLength = incrementalLength,
                ChunkCount = chunkCount
            };

            await using var db = await parent.States.GetCurrentStateDbContext();
            await db.BinaryProperties.AddAsync(bm);
            await db.SaveChangesAsync();
        }

        //public async Task<BinaryMetadata> GetBinaryMetadataAsync(BinaryHash bh)
        //{
        //    var dto = await bmTable.GetEntityAsync<BinaryMetadataDto>(bh.Value, BinaryMetadataDto.ROW_KEY);
        //    var bm = ConvertFromDto(dto);
        //    return bm;
        //}

        public async Task<bool> ExistsAsync(BinaryHash bh)
        {
            await using var db = await parent.States.GetCurrentStateDbContext();
            return await db.BinaryProperties.AnyAsync(bm => bm.Hash == bh);
        }

        /// <summary>
        /// Get the count of (distinct) BinaryHashes
        /// </summary>
        /// <returns></returns>
        public async Task<int> CountAsync()
        {
            await using var db = await parent.States.GetCurrentStateDbContext();
            return await db.PointerFileEntries
                .Select(pfe => pfe.BinaryHash)
                .Distinct()
                .CountAsync();
        }

        /// <summary>
        /// Get all the (distinct) BinaryHashes
        /// </summary>
        /// <returns></returns>
        public async Task<BinaryHash[]> GetAllBinaryHashesAsync()
        {
            await using var db = await parent.States.GetCurrentStateDbContext();
            return await db.PointerFileEntries
                .Select(pfe => pfe.BinaryHash)
                .Distinct()
                .ToArrayAsync();
        }





        internal async Task<ChunkHash[]> GetChunkHashesAsync(BinaryHash binaryHash)
        {
            logger.LogInformation($"Getting chunks for binary {binaryHash.Value}");
            var chunkHashes = Array.Empty<ChunkHash>();

            try
            {
                var ms = new MemoryStream();

                var bc = container.GetBlobClient(GetChunkListBlobName(binaryHash));

                await bc.DownloadToAsync(ms);
                ms.Position = 0;
                chunkHashes = (await JsonSerializer.DeserializeAsync<IEnumerable<string>>(ms))!.Select(hv => new ChunkHash(hv)).ToArray();

                return chunkHashes;
            }
            catch (Azure.RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
            {
                throw new InvalidOperationException($"ChunkList for '{binaryHash}' does not exist");
            }
            finally
            {
                logger.LogInformation($"Getting chunks for binary {binaryHash.Value}... found {chunkHashes.Length} chunk(s)");
            }
        }

        internal async Task CreateChunkHashListAsync(BinaryHash binaryHash, ChunkHash[] chunkHashes)
        {
            var bc = container.GetBlobClient(GetChunkListBlobName(binaryHash));

            if (await bc.ExistsAsync())
                throw new InvalidOperationException($"ChunkList for '{binaryHash}' already Exists");

            var json = JsonSerializer.Serialize(chunkHashes.Select(cf => cf.Value)); //TODO as async?
            var bytes = Encoding.UTF8.GetBytes(json);
            var ms = new MemoryStream(bytes);

            await bc.UploadAsync(ms, new BlobUploadOptions { AccessTier = AccessTier.Cool });
        }

        private string GetChunkListBlobName(BinaryHash binaryHash) => $"{ChunkListsFolderName}/{binaryHash.Value}";

        internal const string ChunkListsFolderName = "chunklists";
    }
}