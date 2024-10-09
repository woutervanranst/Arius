using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Azure.Storage.Blobs.Models;
using ConcurrentCollections;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Archive;

internal partial class ArchiveCommand
{
    private class IndexBlock : TaskBlockBase<DirectoryInfo>
    {
        private readonly ArchiveCommandStatistics                                  stats;
        private readonly bool                                                      fastHash;
        private readonly FileService                                               fileService;
        private readonly FileSystemService                                         fileSystemService;
        private readonly Repository                                                repo;
        private readonly int                                                       maxDegreeOfParallelism;
        private readonly TaskCompletionSource                                      binaryFileUploadCompletedTaskCompletionSource;
        private readonly Func<PointerFile, Task>                                   onIndexedPointerFile;
        private readonly Func<(BinaryFile BinaryFile, bool AlreadyBackedUp), Task> onIndexedBinaryFile;
        private readonly Action                                                    onBinaryFileIndexCompleted;

        public IndexBlock(ArchiveCommand command,
            Func<DirectoryInfo> sourceFunc,
            Action onCompleted,

            int maxDegreeOfParallelism,
            ArchiveCommandOptions options,
            FileService fileService,
            Func<PointerFile, Task> onIndexedPointerFile,
            Func<(BinaryFile BinaryFile, bool AlreadyBackedUp), Task> onIndexedBinaryFile,
            Action onBinaryFileIndexCompleted,
            TaskCompletionSource binaryFileUploadCompletedTaskCompletionSource) 
                : base(command.loggerFactory, sourceFunc, onCompleted)
        {
            this.stats                                         = command.stats;
            this.fileSystemService                             = command.fileSystemService;
            this.repo                                          = command.repo;
            this.fileService                                   = fileService;
            this.fastHash                                      = options.FastHash;
            this.maxDegreeOfParallelism                        = maxDegreeOfParallelism;
            this.binaryFileUploadCompletedTaskCompletionSource = binaryFileUploadCompletedTaskCompletionSource;
            this.onIndexedPointerFile                          = onIndexedPointerFile;
            this.onIndexedBinaryFile                           = onIndexedBinaryFile;
            this.onBinaryFileIndexCompleted                    = onBinaryFileIndexCompleted;
        }

        protected override async Task TaskBodyImplAsync(DirectoryInfo root)
        {
            var latentPointers = new ConcurrentQueue<PointerFile>();
            var binariesThatWillBeUploaded = new ConcurrentHashSet<BinaryHash>();

            await Parallel.ForEachAsync(fileSystemService.GetAllFileInfos(root),
                new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                async (fib, ct) =>
                {
                    try
                    {
                        if (fib is PointerFileInfo pfi)
                        {
                            // PointerFile
                            var pf = fileService.GetExistingPointerFile(root, pfi);
                            
                            logger.LogInformation($"Found PointerFile {pf}");
                            stats.AddLocalRepositoryStatistic(beforePointerFiles: 1);

                            if (await repo.BinaryExistsAsync(pf.BinaryHash))
                                // The pointer points to an existing binary
                                await onIndexedPointerFile(pf);
                            else
                                // The pointer does not have a binary (yet) -- this is an edge case eg when re-uploading an entire archive
                                latentPointers.Enqueue(pf);
                        }
                        else if (fib is BinaryFileInfo bfi)
                        {
                            // BinaryFile
                            var bf = await fileService.GetExistingBinaryFileAsync(root, bfi, fastHash);

                            logger.LogInformation($"Found BinaryFile {bf}");
                            stats.AddLocalRepositoryStatistic(beforeFiles: 1, beforeSize: bf.Length);

                            var pf = fileService.GetExistingPointerFile(bf);
                            if (pf is not null)
                            {
                                if (pf.BinaryHash != bf.BinaryHash)
                                    throw new InvalidOperationException($"The PointerFile {pf} is not valid for the BinaryFile '{bf.FullName}' (BinaryHash does not match). Has the BinaryFile been updated? Delete the PointerFile and try again.");

                                if (!await repo.BinaryExistsAsync(bf.BinaryHash)) //TODO this is a choke point for large state files -- the hashing could already go ahead?
                                {
                                    logger.LogWarning($"BinaryFile {bf} has a PointerFile that points to a nonexisting (remote) Binary ('{bf.BinaryHash}'). Uploading binary again.");
                                    await onIndexedBinaryFile((bf, AlreadyBackedUp: false));
                                }
                                else
                                {
                                    //An equivalent PointerFile already exists and is already being sent through the pipe to have a PointerFileEntry be created - skip.

                                    logger.LogInformation($"BinaryFile {bf} already has a PointerFile that is being processed. Skipping BinaryFile.");
                                    await onIndexedBinaryFile((bf, AlreadyBackedUp: true));
                                }
                            }
                            else
                            {
                                // No PointerFile -- to process
                                await onIndexedBinaryFile((bf, AlreadyBackedUp: false));
                            }

                            binariesThatWillBeUploaded.Add(bf.BinaryHash);
                        }
                        else
                            throw new NotImplementedException();
                    }
                    catch (IOException e) when (e.Message.Contains("virus"))
                    {
                        logger.LogWarning($"Could not back up '{fib.FullName}' because '{e.Message}'");
                    }
                });

            //Wait until all binaries 
            onBinaryFileIndexCompleted();
            await binaryFileUploadCompletedTaskCompletionSource.Task;

            //Iterate over all 'stale' pointers (pointers that were present but did not have a remote binary
            await Parallel.ForEachAsync(latentPointers.GetConsumingEnumerable(),
                new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                async (pf, ct) =>
                {
                    if (!binariesThatWillBeUploaded.Contains(pf.BinaryHash)) //TODO test: create a pointer that points to a nonexisting binary
                        throw new InvalidOperationException($"PointerFile {pf.RelativeName} exists on disk but no corresponding binary exists either locally or remotely.");

                    await onIndexedPointerFile(pf);
                });
        }
    }


    private class UploadBinaryFileBlock : ChannelTaskBlockBase<BinaryFile>
    {
        public UploadBinaryFileBlock(ArchiveCommand command,
            Func<ChannelReader<BinaryFile>> sourceFunc,
            int maxDegreeOfParallelism,
            Action onCompleted,

            ArchiveCommandOptions options,
            IHashValueProvider hashValueProvider,
            Func<BinaryFile, Task> onBinaryExists)
            : base(command.loggerFactory, sourceFunc, maxDegreeOfParallelism, onCompleted)
        {
            this.stats   = command.stats;
            this.repo    = command.repo;
            this.options = options;

            chunker = new ByteBoundaryChunker(hashValueProvider);

            this.onBinaryExists = onBinaryExists;
        }

        private readonly ArchiveCommandStatistics stats;
        private readonly Repository               repo;
        private readonly ArchiveCommandOptions   options;
        private readonly Chunker                  chunker;

        private readonly Func<BinaryFile, Task> onBinaryExists;

        private readonly ConcurrentDictionary<BinaryHash, Task<bool>>           remoteBinaries    = new();
        private readonly ConcurrentDictionary<BinaryHash, TaskCompletionSource> uploadingBinaries = new();
        private readonly ConcurrentDictionary<ChunkHash, TaskCompletionSource>  uploadingChunks   = new();


        protected override async Task ForEachBodyImplAsync(BinaryFile bf, CancellationToken ct)
        {
            /* 
            * This BinaryFile does not yet have an equivalent PointerFile and may need to be uploaded.
            * Four possibilities:
            *   1.   [At the start of the run] the Binary already exist remotely
            *   2.1. [At the start of the run] the Binary did not yet exist remotely, and upload has not started --> upload it 
            *   2.2. [At the start of the run] the Binary did not yet exist remotely, and upload has started but not completed --> wait for it to complete
            *   2.3. [At the start of the run] the Binary did not yet exist remotely, and upload has completed --> continue
            */

            if (options.RemoveLocal) // TODO NOTE THIS BLOCK CAN BE DELETED IF THE ARCHIVE STATISTICS FUNCIONALITY WORKS FINE
                stats.AddLocalRepositoryStatistic(deltaFiles: 0, deltaSize: 0); //Do not add -1 it here, it is set in DeleteBinaryFilesBlock after successful deletion
            else
                stats.AddLocalRepositoryStatistic(deltaFiles: 0, deltaSize: 0); //if we're keeping the local binaries, there are no deltas due to the archive operation

            // [Concurrently] Build a local cache of the remote binaries -- ensure we call BinaryExistsAsync only once
            var binaryExistsRemote = await remoteBinaries.GetOrAdd(bf.BinaryHash, async _ => await repo.BinaryExistsAsync(bf.BinaryHash)); //TODO since this is now backed by a database, we do not need to cache this locally?
            if (binaryExistsRemote)
            {
                // 1 Exists remote
                logger.LogInformation($"Binary for {bf} already exists. No need to upload.");
            }
            else
            {
                // 2 Did not exist remote before the run -- ensure we start the upload only once

                /* TryAdd returns true if the new value was added
                 * ALWAYS create a new TaskCompletionSource with the RunContinuationsAsynchronously option, otherwise the continuations will run on THIS thread -- https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md#always-create-taskcompletionsourcet-with-taskcreationoptionsruncontinuationsasynchronously
                 */
                var binaryToUpload = uploadingBinaries.TryAdd(bf.BinaryHash, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                if (binaryToUpload)
                {
                    // 2.1 Does not yet exist remote and not yet being created --> upload
                    logger.LogInformation($"Binary for {bf} does not exist remotely. To upload and create pointer.");

                    var ce = await UploadAsync(bf);

                    stats.AddRemoteRepositoryStatistic(deltaBinaries: 1, deltaSize: ce.IncrementalLength);

                    uploadingBinaries[bf.BinaryHash].SetResult();
                }
                else
                {
                    var t = uploadingBinaries[bf.BinaryHash].Task;
                    if (!t.IsCompleted)
                    {
                        // 2.2 Did not exist remote but is being created
                        logger.LogInformation($"Binary for {bf} does not exist remotely but is being uploaded. Wait for upload to finish.");

                        await t;
                    }
                    else
                    {
                        // 2.3  Did not exist remote but is created in the mean time
                        logger.LogInformation($"Binary for {bf} did not exist remotely but was uploaded in the mean time.");
                    }
                }
            }

            await onBinaryExists(bf);
        }

        /// <summary>
        /// Upload the given BinaryFile with the specified options
        /// </summary>
        private async Task<ChunkEntry> UploadAsync(BinaryFile bf)
        {
            logger.LogInformation($"Uploading Binary '{bf.Name}' ('{bf.BinaryHash}') of {bf.Length.GetBytesReadable()}...");

            // Upload the Binary
            var (MBps, Mbps, seconds, ce) = await new Stopwatch().GetSpeedAsync(bf.Length, async () =>
            {
                if (options.Dedup) // TODO rewrite as strategy pattern?
                    return await UploadChunkedBinaryAsync(bf);
                else
                    return await repo.UploadChunkAsync(bf, options.Tier);
            });

            logger.LogInformation($"Uploading Binary '{bf.Name}' ('{bf.BinaryHash}') of {bf.Length.GetBytesReadable()}... Completed in {seconds}s ({MBps} MBps / {Mbps} Mbps)");

            return ce;
        }

        /// <summary>
        /// Chunk the BinaryFile then upload all the chunks in parallel
        /// </summary>
        private async Task<ChunkEntry> UploadChunkedBinaryAsync(BinaryFile bf)
        {
            var chunksToUpload         = Channel.CreateBounded<IChunk>(new BoundedChannelOptions(options.TransferChunked_ChunkBufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = true, SingleReader = false }); //limit the capacity of the collection -- backpressure
            var chs                    = new List<ChunkHash>(); //ChunkHashes for this BinaryFile
            var totalArchivedLength    = 0L;
            var totalIncrementalLength = 0L;

            // Design choice: deliberately splitting the chunking section (which cannot be parallelized since we need the chunks in order) and the upload section (which can be paralellelized)
            var chunkTask = Task.Run(async () =>
            {
                await using var binaryFileStream = await bf.OpenReadAsync();

                var (MBps, _, seconds) = await new Stopwatch().GetSpeedAsync(bf.Length, async () =>
                {
                    foreach (var chunk in chunker.Chunk(binaryFileStream))
                    {
                        await chunksToUpload.Writer.WriteAsync(chunk);
                        chs.Add(chunk.ChunkHash);
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
                new ParallelOptions { MaxDegreeOfParallelism = options.TransferChunked_ParallelChunkTransfers },
                async (chunk, cancellationToken) =>
                {
                    var i = Interlocked.Add(ref degreeOfParallelism, 1); // store in variable that is local since threads will ramp up and set the dop value to much higher before the next line is hit
                    logger.LogDebug($"Starting chunk upload '{chunk.ChunkHash}' for {bf.Name}. Current parallelism {i}, remaining queue depth: {chunksToUpload.Reader.Count}");

                    //TODO: while the chance is infinitesimally low, implement like the manifests to avoid that a duplicate chunk will start a upload right after each other
                    /* TODO implement as TryAdd(chunk.ChunkHash)

                         private static readonly SemaphoreSlim mutex = new SemaphoreSlim(1, 1);

                        public static async Task<bool> TryAddAsync(this DbSet<ByteHashValue> set, byte[] value, DbContext context)
                        {
                            await mutex.WaitAsync(); // Acquire the mutex

                            try
                            {
                                const string sql = @"INSERT OR IGNORE INTO HashValues(Value) VALUES (@value);";
                                var param = new DbParameter[] { new Microsoft.Data.Sqlite.SqliteParameter("@value", value) };
                                var affectedRows = await context.Database.ExecuteSqlRawAsync(sql, param);
                                return affectedRows == 1;
                            }
                            finally
                            {
                                mutex.Release(); // Release the mutex
                            }
                        }
                     */
                    if (await repo.ChunkExistsAsync(chunk.ChunkHash))
                    {
                        // 1 Exists remote
                        logger.LogDebug($"Chunk with hash '{chunk.ChunkHash}' already exists. No need to upload.");

                        var ce = await repo.GetChunkEntryAsync(chunk.ChunkHash);
                        Interlocked.Add(ref totalArchivedLength, ce.ArchivedLength);
                        Interlocked.Add(ref totalIncrementalLength, 0);
                    }
                    else
                    {
                        var toUpload = uploadingChunks.TryAdd(chunk.ChunkHash, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                        if (toUpload)
                        {
                            // 2 Does not yet exist remote and not yet being created --> upload
                            logger.LogDebug($"Chunk with hash '{chunk.ChunkHash}' does not exist remotely. To upload.");

                            var ce = await repo.UploadChunkAsync(chunk, options.Tier);
                            Interlocked.Add(ref totalArchivedLength, ce.ArchivedLength);
                            Interlocked.Add(ref totalIncrementalLength, ce.IncrementalLength);

                            uploadingChunks[chunk.ChunkHash].SetResult();
                        }
                        else
                        {
                            // 3 Does not exist remote but is being created by another thread
                            logger.LogDebug($"Chunk with hash '{chunk.ChunkHash}' does not exist remotely but is already being uploaded. Wait for its creation.");

                            await uploadingChunks[chunk.ChunkHash].Task;

                            var ce = await repo.GetChunkEntryAsync(chunk.ChunkHash);
                            Interlocked.Add(ref totalArchivedLength, ce.ArchivedLength);
                            Interlocked.Add(ref totalIncrementalLength, 0);

                            //TODO Write unit test for this path
                        }
                    }

                    Interlocked.Add(ref degreeOfParallelism, -1);
                });
            await chunkTask; //this task will always be compete at this point

            if (chs.Count > 1)
            {
                // If the file has been chunked in multiple chunks, create a ChunkList and a separate Binary ChunkEntry
                await repo.CreateChunkListAsync(bf.BinaryHash, chs);
                return await repo.CreateChunkEntryAsync(bf, totalArchivedLength, 0 /* the incrementalLength is in the ChunkEntries of the Chunks */, chs.Count, null /* accesstier is undefined for a chunked binary */);
            }
            else
            {
                return await repo.GetChunkEntryAsync(bf.BinaryHash);
            }
        }
    }



    private class CreatePointerFileIfNotExistsBlock : ChannelTaskBlockBase<BinaryFile>
    {
        public CreatePointerFileIfNotExistsBlock(ArchiveCommand command,
            Func<ChannelReader<BinaryFile>> sourceFunc,
            int maxDegreeOfParallelism,
            Action onCompleted,

            FileService fileService,
            Func<BinaryFile, Task> onSuccesfullyBackedUp,
            Func<PointerFile, Task> onPointerFileCreated)
                : base(command.loggerFactory, sourceFunc, maxDegreeOfParallelism, onCompleted)
        {
            this.stats                 = command.stats;
            this.fileService           = fileService;
            this.onSuccesfullyBackedUp = onSuccesfullyBackedUp;
            this.onPointerFileCreated  = onPointerFileCreated;
        }

        private readonly ArchiveCommandStatistics stats;
        private readonly FileService fileService;
        private readonly Func<BinaryFile, Task> onSuccesfullyBackedUp;
        private readonly Func<PointerFile, Task> onPointerFileCreated;

        protected override async Task ForEachBodyImplAsync(BinaryFile bf, CancellationToken ct)
        {
            logger.LogDebug($"Creating pointer for {bf}...");

            var (created, pf) = fileService.CreatePointerFileIfNotExists(bf);

            logger.LogInformation($"Creating pointer for {bf}... done");
            if (created)
                stats.AddLocalRepositoryStatistic(deltaPointerFiles: 1);

            await onSuccesfullyBackedUp(bf);
            await onPointerFileCreated(pf);
        }
    }
    

    private class CreatePointerFileEntryIfNotExistsBlock : ChannelTaskBlockBase<PointerFile>
    {
        public CreatePointerFileEntryIfNotExistsBlock(ArchiveCommand command,
            Func<ChannelReader<PointerFile>> sourceFunc,
            int maxDegreeOfParallelism,
            Action onCompleted,

            ArchiveCommandOptions options) 
                : base(command.loggerFactory, sourceFunc, maxDegreeOfParallelism, onCompleted)
        {
            this.stats      = command.stats;
            this.repo       = command.repo;
            this.versionUtc = options.VersionUtc;
        }

        private readonly ArchiveCommandStatistics stats;
        private readonly Repository               repo;
        private readonly DateTime                 versionUtc;

        protected override async Task ForEachBodyImplAsync(PointerFile pointerFile, CancellationToken ct)
        {
            logger.LogDebug($"Upserting PointerFile entry for {pointerFile}...");

            var r = await repo.CreatePointerFileEntryIfNotExistsAsync(pointerFile, versionUtc);

            switch (r)
            {
                case Repository.CreatePointerFileEntryResult.Inserted:
                    logger.LogInformation($"Upserting PointerFile entry for {pointerFile}... done. Inserted entry.");
                    stats.AddRemoteRepositoryStatistic(deltaPointerFileEntries: 1);
                    break;
                case Repository.CreatePointerFileEntryResult.InsertedDeleted:
                    // TODO IS THIS EVER HIT?? I dont think so - the deleted entry is created in CreateDeletedPointerFileEntryForDeletedPointerFilesBlock
                    logger.LogInformation($"Upserting PointerFile entry for {pointerFile}... done. Inserted 'deleted' entry.");
                    stats.AddRemoteRepositoryStatistic(deltaPointerFileEntries: 1); //note this is PLUS 1 since we re adding an entry really
                    break;
                case Repository.CreatePointerFileEntryResult.NoChange:
                    logger.LogDebug($"Upserting PointerFile entry for {pointerFile}... done. No change made, latest entry was up to date.");
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
    }


    private class DeleteBinaryFilesBlock : ChannelTaskBlockBase<BinaryFile>
    {
        public DeleteBinaryFilesBlock(ArchiveCommand command,
            Func<ChannelReader<BinaryFile>> sourceFunc,
            int maxDegreeOfParallelism,
            Action onCompleted)
                : base(command.loggerFactory, sourceFunc, maxDegreeOfParallelism, onCompleted)
        {
            this.stats = command.stats;
        }

        private readonly ArchiveCommandStatistics stats;

        protected override Task ForEachBodyImplAsync(BinaryFile bf, CancellationToken ct)
        { 
            if (File.Exists(bf.FullName))
            {
                logger.LogDebug($"RemoveLocal flag is set - Deleting binary {bf}...");
                stats.AddLocalRepositoryStatistic(deltaFiles: -1, deltaSize: bf.Length * -1); //NOTE this is before the Delete() call because bf.Length does not work on an unexisting file //TODO test icw  

                bf.Delete();

                logger.LogInformation($"RemoveLocal flag is set - Deleting binary {bf}... done");
            }

            return Task.CompletedTask;
        }
    }


    private class CreateDeletedPointerFileEntryForDeletedPointerFilesBlock : ChannelTaskBlockBase<PointerFileEntry>
    {
        public CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(ArchiveCommand command,
            Func<Task<ChannelReader<PointerFileEntry>>> sourceFunc,
            int maxDegreeOfParallelism,
            Action onCompleted,

            ArchiveCommandOptions options,
            FileService fileService)
                : base(command.loggerFactory, sourceFunc, maxDegreeOfParallelism, onCompleted)
        {
            this.stats       = command.stats;
            this.repo        = command.repo;
            this.fileService = fileService;
            this.root        = options.Path;
            this.versionUtc  = options.VersionUtc;
        }

        private readonly ArchiveCommandStatistics stats;
        private readonly Repository repo;
        private readonly FileService fileService;
        private readonly DirectoryInfo root;
        private readonly DateTime versionUtc;

        protected override async Task ForEachBodyImplAsync(PointerFileEntry pfe, CancellationToken ct)
        {
            if (!pfe.IsDeleted &&
                fileService.GetExistingPointerFile(root, pfe) is null &&
                await fileService.GetExistingBinaryFileAsync(root, pfe, assertHash: false) is null) //PointerFileEntry is marked as exists and there is no PointerFile and there is no BinaryFile (only on PointerFile may not work since it may still be in the pipeline to be created)
            {
                logger.LogInformation($"The pointer or binary for '{pfe}' no longer exists locally, marking entry as deleted");
                stats.AddLocalRepositoryStatistic(deltaPointerFiles: -1);
                stats.AddRemoteRepositoryStatistic(deltaPointerFileEntries: 1); //note this is PLUS 1 since we re adding an entry really

                await repo.CreateDeletedPointerFileEntryAsync(pfe, versionUtc);
            }
        }
    }


    private class UpdateTierBlock : TaskBlockBase
    {
        public UpdateTierBlock(ArchiveCommand command,
            Func<Repository> sourceFunc,
            Action onCompleted,

            int maxDegreeOfParallelism,
            ArchiveCommandOptions options) 
                : base(command.loggerFactory, onCompleted)
        {
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.repo                   = command.repo;
            this.targetAccessTier       = options.Tier;
        }

        private readonly int maxDegreeOfParallelism;
        private readonly Repository repo;
        private readonly AccessTier targetAccessTier;

        protected override async Task TaskBodyImplAsync()
        {
            await repo.UpdateAllChunksToTier(targetAccessTier, maxDegreeOfParallelism);
        }
    }
}
    