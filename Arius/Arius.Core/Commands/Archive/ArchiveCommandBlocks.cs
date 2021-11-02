using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Arius.Core.Commands.Archive;

internal class IndexBlock : TaskBlockBase<DirectoryInfo>
{
    public IndexBlock(ILoggerFactory loggerFactory,
        Func<DirectoryInfo> sourceFunc,
        int maxDegreeOfParallelism,
        bool fastHash,
        PointerService pointerService,
        Repository repo,
        Action<PointerFile> indexedPointerFile,
        Action<(BinaryFile BinaryFile, bool AlreadyBackedUp)> indexedBinaryFile,
        IHashValueProvider hvp,
        Action done)
        : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, done: done)
    {
        this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        this.fastHash = fastHash;
        this.pointerService = pointerService;
        this.repo = repo;
        this.indexedPointerFile = indexedPointerFile;
        this.indexedBinaryFile = indexedBinaryFile;
        this.hvp = hvp;
    }

    private readonly int maxDegreeOfParallelism;
    private readonly bool fastHash;
    private readonly PointerService pointerService;
    private readonly Repository repo;
    private readonly Action<PointerFile> indexedPointerFile;
    private readonly Action<(BinaryFile BinaryFile, bool AlreadyBackedUp)> indexedBinaryFile;
    private readonly IHashValueProvider hvp;

    protected override async Task TaskBodyImplAsync(DirectoryInfo root)
    {
        foreach (var fi in root.GetAllFileInfos(logger)
                     .AsParallel()
                     .WithDegreeOfParallelism(maxDegreeOfParallelism))
        {
            try
            {
                var rn = fi.GetRelativeName(root);

                if (fi.IsPointerFile())
                {
                    //fi is a PointerFile
                    logger.LogInformation($"Found PointerFile '{rn}'");

                    var pf = pointerService.GetPointerFile(root, fi);

                    indexedPointerFile(pf);
                }
                else
                {
                    //fi is a BinaryFile
                    logger.LogInformation($"Found BinaryFile '{rn}'. Hashing...");

                    //Get the Hash for this file
                    BinaryHash binaryHash = default;
                    var pf = pointerService.GetPointerFile(root, fi);
                    if (fastHash && pf is not null)
                    {
                        //A corresponding PointerFile exists
                        binaryHash = pf.Hash;

                        logger.LogInformation($"Hashing BinaryFile '{rn}'... done with fasthash. Hash: '{binaryHash.ToShortString()}'");
                    }
                    else
                    {
                        var (MBps, _, seconds) = new Stopwatch().GetSpeed(fi.Length, () =>
                        {
                            binaryHash = hvp.GetBinaryHash(fi);
                        });

                        logger.LogInformation($"Hashing BinaryFile '{rn}'... done in {seconds}s at {MBps} MBps. Hash: '{binaryHash.ToShortString()}'");
                    }

                    var bf = new BinaryFile(root, fi, binaryHash);
                    if (pf is not null)
                    {
                        if (pf.Hash == binaryHash)
                        {
                            if (!await repo.BinaryExistsAsync(binaryHash))
                            {
                                logger.LogWarning($"BinaryFile '{bf.RelativeName}' has a PointerFile that points to a nonexisting (remote) Binary ('{binaryHash.ToShortString()}'). Uploading binary again.");
                                indexedBinaryFile((bf, AlreadyBackedUp: false));
                            }
                            else
                            {
                                //An equivalent PointerFile already exists and is already being sent through the pipe - skip.

                                logger.LogInformation($"BinaryFile '{bf.RelativeName}' already has a PointerFile that is being processed. Skipping BinaryFile.");
                                indexedBinaryFile((bf, AlreadyBackedUp: true));
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException($"The PointerFile '{pf.FullName}' is not valid for the BinaryFile '{bf.FullName}' (BinaryHash does not match). Has the BinaryFile been updated? Delete the PointerFile and try again.");
                        }
                    }
                    else
                    {
                        // No PointerFile -- to process
                        indexedBinaryFile((bf, AlreadyBackedUp: false));
                    }
                }
            }
            catch (IOException e) when (e.Message.Contains("virus"))
            {
                logger.LogWarning($"Could not back up '{fi.FullName}' because '{e.Message}'");
            }
        }
    }
}


internal class UploadBinaryFileBlock : BlockingCollectionTaskBlockBase<BinaryFile>
{
    public UploadBinaryFileBlock(
        ILoggerFactory loggerFactory,
        Func<BlockingCollection<BinaryFile>> sourceFunc,
        int degreeOfParallelism,
        Chunker chunker,
        Repository repo,
        ArchiveCommandOptions options,
            
        Action<BinaryFile> binaryExists,
        Action done) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, degreeOfParallelism: degreeOfParallelism, done: done)
    {
        this.chunker = chunker;
        this.repo = repo;
        this.options = options;
        this.binaryExists = binaryExists;
    }

    private readonly Chunker chunker;
    private readonly Repository repo;
    private readonly ArchiveCommandOptions options;
        
    private readonly Action<BinaryFile> binaryExists;

    private readonly ConcurrentDictionary<BinaryHash, Task<bool>> remoteBinaries = new();
    private readonly ConcurrentDictionary<BinaryHash, TaskCompletionSource> uploadingBinaries = new();
    private readonly ConcurrentDictionary<ChunkHash, TaskCompletionSource> uploadingChunks = new();
        

    protected override async Task ForEachBodyImplAsync(BinaryFile bf)
    {
        /* 
        * This BinaryFile does not yet have an equivalent PointerFile and may need to be uploaded.
        * Four possibilities:
        *   1.  [At the start of the run] the Binary already exist remotely
        *   2.1. [At the start of the run] the Binary did not yet exist remotely, and upload has not started --> upload it 
        *   2.2. [At the start of the run] the Binary did not yet exist remotely, and upload has started but not completed --> wait for it to complete
        *   2.3. [At the start of the run] the Binary did not yet exist remotely, and upload has completed --> continue
        */

        // [Concurrently] Build a local cache of the remote binaries -- ensure we call BinaryExistsAsync only once
        var binaryExistsRemote = await remoteBinaries.GetOrAdd(bf.Hash, async (_) => await repo.BinaryExistsAsync(bf.Hash));
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
            var binaryToUpload = uploadingBinaries.TryAdd(bf.Hash, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)); 
            if (binaryToUpload)
            {
                // 2.1 Does not yet exist remote and not yet being created --> upload
                logger.LogInformation($"Binary for {bf} does not exist remotely. To upload and create pointer.");

                await UploadBinaryAsync(bf);

                uploadingBinaries[bf.Hash].SetResult();
            }
            else
            {
                var t = uploadingBinaries[bf.Hash].Task;
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
            
        binaryExists(bf);
    }

    private async Task UploadBinaryAsync(BinaryFile bf)
    {
        logger.LogInformation($"Uploading {bf.Length.GetBytesReadable()} of '{bf.Name}' ('{bf.Hash.ToShortString()}')...");

        // Upload the Binary
        var (MBps, Mbps, seconds, chs, totalLength, incrementalLength) = await new Stopwatch().GetSpeedAsync(bf.Length, async () => 
        {
            return options.Dedup switch
            {
                true => await UploadChunkedBinaryAsync(bf),
                false => await UploadBinaryChunkAsync(bf)
            };
        });
            
        logger.LogInformation($"Uploading {bf.Length.GetBytesReadable()} of {bf}... Completed in {seconds}s ({MBps} MBps / {Mbps} Mbps)");

        // Create the BinaryManifest
        await repo.CreateBinaryManifestAsync(bf.Hash, chs);

        // Create the BinaryMetadata
        await repo.CreateBinaryMetadataAsync(bf, totalLength, incrementalLength, chs.Length);
    }

        
    /// <summary>
    /// Chunk the BinaryFile then upload all the chunks in parallel
    /// </summary>
    /// <param name="bf"></param>
    /// <returns></returns>
    private async Task<(ChunkHash[], long totalLength, long incrementalLength)> UploadChunkedBinaryAsync(BinaryFile bf)
    {
        var chunksToUpload = Channel.CreateBounded<IChunk>(new BoundedChannelOptions(options.UploadBinaryFileBlock_ChunkBufferSize) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = true, SingleReader = false }); //limit the capacity of the collection -- backpressure
        var chs = new List<ChunkHash>(); //ChunkHashes for this BinaryFile
        var totalLength = 0L;
        var incrementalLength = 0L;

        // Design choice: deliberately splitting the chunking section (which cannot be parallelized since we need the chunks in order) and the upload section (which can be paralellelized)
        var t = Task.Run(async () =>
        {
            using var binaryFileStream = await bf.OpenReadAsync();

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
            new ParallelOptions { MaxDegreeOfParallelism = options.UploadBinaryFileBlock_ParallelChunkUploads }, 
            async (chunk, cancellationToken) => 
            {
                var i = Interlocked.Add(ref degreeOfParallelism, 1); // store in variable that is local since threads will ramp up and set the dop value to much higher before the next line is hit
                logger.LogDebug($"Starting chunk upload '{chunk.Hash.ToShortString()}' for {bf.Name}. Current parallelism {i}, remaining queue depth: {chunksToUpload.Reader.Count}");


                if (await repo.ChunkExistsAsync(chunk.Hash)) //TODO: while the chance is infinitesimally low, implement like the manifests to avoid that a duplicate chunk will start a upload right after each other
                {
                    // 1 Exists remote
                    logger.LogDebug($"Chunk with hash '{chunk.Hash.ToShortString()}' already exists. No need to upload.");

                    var length = repo.GetChunkBlobByHash(chunk.Hash, false).Length;
                    Interlocked.Add(ref totalLength, length);
                    Interlocked.Add(ref incrementalLength, 0);
                }
                else
                {
                    bool toUpload = uploadingChunks.TryAdd(chunk.Hash, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                    if (toUpload)
                    {
                        // 2 Does not yet exist remote and not yet being created --> upload
                        logger.LogDebug($"Chunk with hash '{chunk.Hash.ToShortString()}' does not exist remotely. To upload.");

                        var length = await repo.UploadChunkAsync(chunk, options.Tier);
                        Interlocked.Add(ref totalLength, length);
                        Interlocked.Add(ref incrementalLength, length);

                        uploadingChunks[chunk.Hash].SetResult();
                    }
                    else
                    {
                        // 3 Does not exist remote but is being created by another thread
                        logger.LogDebug($"Chunk with hash '{chunk.Hash.ToShortString()}' does not exist remotely but is already being uploaded. Wait for its creation.");

                        await uploadingChunks[chunk.Hash].Task;

                        var length = repo.GetChunkBlobByHash(chunk.Hash, false).Length;
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
    private async Task<(ChunkHash[], long totalLength, long incrementalLength)> UploadBinaryChunkAsync(BinaryFile bf)
    {
        var length = await repo.UploadChunkAsync(bf, options.Tier);

        return (((IChunk)bf).Hash.SingleToArray(), length, length);
    }
}


internal class CreatePointerFileIfNotExistsBlock : BlockingCollectionTaskBlockBase<BinaryFile>
{
    public CreatePointerFileIfNotExistsBlock(ILoggerFactory loggerFactory,
        Func<BlockingCollection<BinaryFile>> sourceFunc,
        int degreeOfParallelism,
        PointerService pointerService,
        Action<BinaryFile> succesfullyBackedUp,
        Action<PointerFile> pointerFileCreated,
        Action done) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, degreeOfParallelism: degreeOfParallelism, done: done)
    {
        this.pointerService = pointerService;
        this.succesfullyBackedUp = succesfullyBackedUp;
        this.pointerFileCreated = pointerFileCreated;
    }

    private readonly PointerService pointerService;
    private readonly Action<BinaryFile> succesfullyBackedUp;
    private readonly Action<PointerFile> pointerFileCreated;

    protected override Task ForEachBodyImplAsync(BinaryFile bf)
    {
        logger.LogInformation($"Creating pointer for '{bf.RelativeName}'...");

        var pf = pointerService.CreatePointerFileIfNotExists(bf);

        logger.LogInformation($"Creating pointer for '{bf.RelativeName}'... done");

        succesfullyBackedUp(bf);
        pointerFileCreated(pf);

        return Task.CompletedTask;
    }
}


internal class CreatePointerFileEntryIfNotExistsBlock : BlockingCollectionTaskBlockBase<PointerFile>
{
    public CreatePointerFileEntryIfNotExistsBlock(ILoggerFactory loggerFactory,
        Func<BlockingCollection<PointerFile>> sourceFunc,
        int degreeOfParallelism,
        Repository repo,
        DateTime versionUtc,
        Action done) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, degreeOfParallelism: degreeOfParallelism, done: done)
    {
        this.repo = repo;
        this.versionUtc = versionUtc;
    }

    private readonly Repository repo;
    private readonly DateTime versionUtc;

    protected override async Task ForEachBodyImplAsync(PointerFile pointerFile)
    {
        logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'...");

        var r = await repo.CreatePointerFileEntryIfNotExistsAsync(pointerFile, versionUtc);

        switch (r)
        {
            case Repository.CreatePointerFileEntryResult.Inserted:
                logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Inserted entry.");
                break;
            case Repository.CreatePointerFileEntryResult.InsertedDeleted:
                logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Inserted 'deleted' entry.");
                break;
            case Repository.CreatePointerFileEntryResult.NoChange:
                logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. No change made, latest entry was up to date.");
                break;
            default:
                throw new NotImplementedException();
        }
    }
}


internal class DeleteBinaryFilesBlock : BlockingCollectionTaskBlockBase<BinaryFile>
{
    public DeleteBinaryFilesBlock(ILoggerFactory loggerFactory,
        Func<BlockingCollection<BinaryFile>> sourceFunc,
        int maxDegreeOfParallelism,
        Action done) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, degreeOfParallelism: maxDegreeOfParallelism, done: done)
    {
    }

    protected override Task ForEachBodyImplAsync(BinaryFile bf)
    {
        logger.LogInformation($"RemoveLocal flag is set - Deleting binary '{bf.RelativeName}'...");
        bf.Delete();
        logger.LogInformation($"RemoveLocal flag is set - Deleting binary '{bf.RelativeName}'... done");

        return Task.CompletedTask;
    }
}


internal class CreateDeletedPointerFileEntryForDeletedPointerFilesBlock : BlockingCollectionTaskBlockBase<PointerFileEntry>
{
    public CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(ILoggerFactory loggerFactory,
        Func<Task<BlockingCollection<PointerFileEntry>>> sourceFunc,
        int degreeOfParallelism,
        Repository repo,
        DirectoryInfo root,
        PointerService pointerService,
        DateTime versionUtc,
        Action done) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, degreeOfParallelism: degreeOfParallelism, done: done)
    {
        this.repo = repo;
        this.root = root;
        this.pointerService = pointerService;
        this.versionUtc = versionUtc;
    }

    private readonly Repository repo;
    private readonly DirectoryInfo root;
    private readonly PointerService pointerService;
    private readonly DateTime versionUtc;

    protected override async Task ForEachBodyImplAsync(PointerFileEntry pfe)
    {
        if (!pfe.IsDeleted &&
            pointerService.GetPointerFile(root, pfe) is null &&
            pointerService.GetBinaryFile(root, pfe, ensureCorrectHash: false) is null) //PointerFileEntry is marked as exists and there is no PointerFile and there is no BinaryFile (only on PointerFile may not work since it may still be in the pipeline to be created)
        {
            logger.LogInformation($"The pointer or binary for '{pfe.RelativeName}' no longer exists locally, marking entry as deleted");
            await repo.CreateDeletedPointerFileEntryAsync(pfe, versionUtc);
        }
    }
}


internal class ExportToJsonBlock : TaskBlockBase<BlockingCollection<PointerFileEntry>> //! must be single threaded hence TaskBlockBase
{
    public ExportToJsonBlock(ILoggerFactory loggerFactory,
        Func<Task<BlockingCollection<PointerFileEntry>>> sourceFunc,
        Repository repo,
        DateTime versionUtc,
        Action done) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, done: done)
    {
        this.repo = repo;
        this.versionUtc = versionUtc;
    }

    private readonly Repository repo;
    private readonly DateTime versionUtc;


    protected override async Task TaskBodyImplAsync(BlockingCollection<PointerFileEntry> source)
    {
        logger.LogInformation($"Writing state to JSON...");

        using var file = File.Create($"arius-state-{versionUtc.ToLocalTime():yyyyMMdd-HHmmss}.json");
        var writer = new Utf8JsonWriter(file, new JsonWriterOptions() { Indented = true });
        writer.WriteStartArray();

        // See https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-converters-how-to

        foreach (var pfe in source
                     //.AsParallel().WithDegreeOfParallelism(8)
                     //.AsEnumerable()) 
                     .GetConsumingEnumerable())
        {
            var chs = await repo.GetChunksForBinaryAsync(pfe.BinaryHash);
            var entry = new PointerFileEntryWithChunkHashes(pfe, chs);

            JsonSerializer.Serialize(writer, entry, new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default });
        }

        writer.WriteEndArray();
        await writer.FlushAsync();

        logger.LogInformation($"Writing state to JSON... done");
    }

    private struct PointerFileEntryWithChunkHashes
    {
        public PointerFileEntryWithChunkHashes(PointerFileEntry pfe, ChunkHash[] chs)
        {
            this.pfe = pfe;
            this.chs = chs;
        }

        private readonly PointerFileEntry pfe;
        private readonly ChunkHash[] chs;

        public string BinaryHash => pfe.BinaryHash.Value;
        public IEnumerable<string> ChunkHashes => chs.Select(h => h.Value);
        public string RelativeName => pfe.RelativeName;
        public DateTime VersionUtc => pfe.VersionUtc;
        public bool IsDeleted => pfe.IsDeleted;
        public DateTime? CreationTimeUtc => pfe.CreationTimeUtc;
        public DateTime? LastWriteTimeUtc => pfe.LastWriteTimeUtc;
    }
}


internal class ValidateBlock
{
    public ValidateBlock(ILogger<ExportToJsonBlock> logger,
        Func<BlockingCollection<PointerFileEntry>> sourceFunc,
        Repository repo,
        DateTime versionUtc,
        Action start,
        Action done)
    {
        //logger.LogInformation($"Validating {pointerFile.FullName}...");

        //logger.LogWarning($"Validating {pointerFile.FullName}... - Not yet implemented");

        ////    // Validate the manifest
        ////    var chunkHashes = await repo.GetChunkHashesAsync(pointerFile.Hash);

        ////    if (!chunkHashes.Any())
        ////        throw new InvalidOperationException($"Manifest {pointerFile.Hash} (of PointerFile {pointerFile.FullName}) contains no chunks");

        ////    double length = 0;
        ////    foreach (var chunkHash in chunkHashes)
        ////    {
        ////        var cb = repo.GetChunkBlobByHash(chunkHash, false);
        ////        length += cb.Length;
        ////    }

        ////    var bfi = pointerFile.BinaryFileInfo;
        ////    if (bfi.Exists)
        ////    {
        ////        //TODO if we would know the EXACT/uncompressed size from the PointerFileEntry - use that
        ////        if (bfi.Length / length < 0.9)
        ////            throw new InvalidOperationException("something is wrong");
        ////    }
        ////    else
        ////    {
        ////        //TODO if we would know the expected size from the PointerFileEntry - use that
        ////        if (length == 0)
        ////            throw new InvalidOperationException("something is wrong");
        ////    }

        //logger.LogInformation($"Validating {pointerFile.FullName}... OK!");
    }

}