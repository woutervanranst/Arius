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
using ConcurrentCollections;

namespace Arius.Core.Commands.Archive;

internal class IndexBlock : TaskBlockBase<DirectoryInfo>
{
    public IndexBlock(ILoggerFactory loggerFactory,
        Func<DirectoryInfo> sourceFunc,
        int maxDegreeOfParallelism,
        bool fastHash,
        PointerService pointerService,
        Repository repo,
        IHashValueProvider hvp,
        TaskCompletionSource binaryFileUploadCompletedTaskCompletionSource,
        Func<PointerFile, Task> onIndexedPointerFile,
        Func<(BinaryFile BinaryFile, bool AlreadyBackedUp), Task> onIndexedBinaryFile,
        Action onBinaryFileIndexCompleted,
        Action onCompleted)
        : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, onCompleted: onCompleted)
    {
        this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        this.fastHash = fastHash;
        this.pointerService = pointerService;
        this.repo = repo;
        this.hvp = hvp;
        this.binaryFileUploadCompletedTaskCompletionSource = binaryFileUploadCompletedTaskCompletionSource;
        this.onIndexedPointerFile = onIndexedPointerFile;
        this.onIndexedBinaryFile = onIndexedBinaryFile;
        this.onBinaryFileIndexCompleted = onBinaryFileIndexCompleted;
    }

    private readonly int maxDegreeOfParallelism;
    private readonly bool fastHash;
    private readonly PointerService pointerService;
    private readonly Repository repo;
    private readonly Func<PointerFile, Task> onIndexedPointerFile;
    private readonly Func<(BinaryFile BinaryFile, bool AlreadyBackedUp), Task> onIndexedBinaryFile;
    private readonly IHashValueProvider hvp;
    private readonly Action onBinaryFileIndexCompleted;
    private readonly TaskCompletionSource binaryFileUploadCompletedTaskCompletionSource;

    protected override async Task TaskBodyImplAsync(DirectoryInfo root)
    {
        //var ka = new ConcurrentDictionary<BinaryHash, (bool exists, ConcurrentBag<PointerFile> ka)>();
        var latentPointers = new ConcurrentQueue<PointerFile>();
        var binariesThatWillBeUploaded = new ConcurrentHashSet<BinaryHash>();

        await Parallel.ForEachAsync(root.GetAllFileInfos(logger),
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            async (fi, ct) =>
            {
                try
                {
                    var pf = pointerService.GetPointerFile(root, fi);

                    if (fi.IsPointerFile())
                    {
                        // PointerFile
                        logger.LogInformation($"Found PointerFile '{pf.RelativeName}'");

                        if (await repo.Binaries.ExistsAsync(pf.Hash))
                            // The pointer points to an existing binary
                            await onIndexedPointerFile(pf);
                        else
                            // The pointer does not have a binary (yet) -- this is an edge case eg when re-uploading an entire archive
                            latentPointers.Enqueue(pf);
                    }
                    else
                    {
                        // BinaryFile
                        var bh = GetBinaryHash(root, fi, pf);
                        var bf = new BinaryFile(root, fi, bh);

                        if (pf is not null)
                        {
                            if (pf.Hash != bh)
                                throw new InvalidOperationException($"The PointerFile '{pf.FullName}' is not valid for the BinaryFile '{bf.FullName}' (BinaryHash does not match). Has the BinaryFile been updated? Delete the PointerFile and try again.");

                            if (!await repo.Binaries.ExistsAsync(bh))
                            {
                                logger.LogWarning($"BinaryFile '{bf.RelativeName}' has a PointerFile that points to a nonexisting (remote) Binary ('{bh.ToShortString()}'). Uploading binary again.");
                                await onIndexedBinaryFile((bf, AlreadyBackedUp: false));
                            }
                            else
                            {
                                //An equivalent PointerFile already exists and is already being sent through the pipe to have a PointerFileEntry be created - skip.

                                logger.LogInformation($"BinaryFile '{bf.RelativeName}' already has a PointerFile that is being processed. Skipping BinaryFile.");
                                await onIndexedBinaryFile((bf, AlreadyBackedUp: true));
                            }
                        }
                        else
                        {
                            // No PointerFile -- to process
                            await onIndexedBinaryFile((bf, AlreadyBackedUp: false));
                        }

                        binariesThatWillBeUploaded.Add(bh);
                    }
                }
                catch (IOException e) when (e.Message.Contains("virus"))
                {
                    logger.LogWarning($"Could not back up '{fi.FullName}' because '{e.Message}'");
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
                if (!binariesThatWillBeUploaded.Contains(pf.Hash)) //TODO test: create a pointer that points to a nonexisting binary
                    throw new InvalidOperationException($"PointerFile {pf.RelativeName} exists on disk but no corresponding binary exists either locally or remotely.");

                await onIndexedPointerFile(pf);
            });
    }
    private BinaryHash GetBinaryHash(DirectoryInfo root, FileInfo fi, PointerFile pf)
    {
        BinaryHash binaryHash = default;
        if (fastHash && pf is not null)
        {
            //A corresponding PointerFile exists and FastHash is TRUE
            binaryHash = pf.Hash; //use the hash from the pointerfile

            logger.LogInformation($"Found BinaryFile '{pf.RelativeName}' with hash '{binaryHash.ToShortString()}' (fasthash)");
        }
        else
        {
            var rn = fi.GetRelativeName(root);

            logger.LogInformation($"Found BinaryFile '{rn}'. Hashing...");

            var (MBps, _, seconds) = new Stopwatch().GetSpeed(fi.Length, () =>
                binaryHash = hvp.GetBinaryHash(fi));

            logger.LogInformation($"Found BinaryFile '{rn}'. Hashing... done in {seconds}s at {MBps} MBps. Hash: '{binaryHash.ToShortString()}'");
        }

        return binaryHash;
        // 
    }
}


internal class UploadBinaryFileBlock : ChannelTaskBlockBase<BinaryFile>
{
    public UploadBinaryFileBlock(
        ILoggerFactory loggerFactory,
        Func<ChannelReader<BinaryFile>> sourceFunc,
        int maxDegreeOfParallelism,
        Repository repo,
        ArchiveCommandOptions options,
            
        Func<BinaryFile, Task> binaryExists,
        Action onCompleted) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, onCompleted: onCompleted)
    {
        this.repo = repo;
        this.options = options;
        this.binaryExists = binaryExists;
    }

    private readonly Repository repo;
    private readonly ArchiveCommandOptions options;
        
    private readonly Func<BinaryFile, Task> binaryExists;

    private readonly ConcurrentDictionary<BinaryHash, Task<bool>> remoteBinaries = new();
    private readonly ConcurrentDictionary<BinaryHash, TaskCompletionSource> uploadingBinaries = new();
        

    protected override async Task ForEachBodyImplAsync(BinaryFile bf, CancellationToken ct)
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
        var binaryExistsRemote = await remoteBinaries.GetOrAdd(bf.Hash, async (_) => await repo.Binaries.ExistsAsync(bf.Hash));
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

                await repo.Binaries.UploadAsync(bf, options);

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
            
        await binaryExists(bf);
    }
}


internal class CreatePointerFileIfNotExistsBlock : ChannelTaskBlockBase<BinaryFile>
{
    public CreatePointerFileIfNotExistsBlock(ILoggerFactory loggerFactory,
        Func<ChannelReader<BinaryFile>> sourceFunc,
        int maxDegreeOfParallelism,
        PointerService pointerService,
        Func<BinaryFile, Task> succesfullyBackedUp,
        Func<PointerFile, Task> pointerFileCreated,
        Action onCompleted) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, onCompleted: onCompleted)
    {
        this.pointerService = pointerService;
        this.succesfullyBackedUp = succesfullyBackedUp;
        this.pointerFileCreated = pointerFileCreated;
    }

    private readonly PointerService pointerService;
    private readonly Func<BinaryFile, Task> succesfullyBackedUp;
    private readonly Func<PointerFile, Task> pointerFileCreated;

    protected override async Task ForEachBodyImplAsync(BinaryFile bf, CancellationToken ct)
    {
        logger.LogInformation($"Creating pointer for '{bf.RelativeName}'...");

        var pf = pointerService.CreatePointerFileIfNotExists(bf);

        logger.LogInformation($"Creating pointer for '{bf.RelativeName}'... done");

        await succesfullyBackedUp(bf);
        await pointerFileCreated(pf);
    }
}


internal class CreatePointerFileEntryIfNotExistsBlock : ChannelTaskBlockBase<PointerFile>
{
    public CreatePointerFileEntryIfNotExistsBlock(ILoggerFactory loggerFactory,
        Func<ChannelReader<PointerFile>> sourceFunc,
        int maxDegreeOfParallelism,
        Repository repo,
        DateTime versionUtc,
        Action onCompleted) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, onCompleted: onCompleted)
    {
        this.repo = repo;
        this.versionUtc = versionUtc;
    }

    private readonly Repository repo;
    private readonly DateTime versionUtc;

    protected override async Task ForEachBodyImplAsync(PointerFile pointerFile, CancellationToken ct)
    {
        logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'...");

        var r = await repo.PointerFileEntries.CreatePointerFileEntryIfNotExistsAsync(pointerFile, versionUtc);

        switch (r)
        {
            case Repository.PointerFileEntryRepository.CreatePointerFileEntryResult.Inserted:
                logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Inserted entry.");
                break;
            case Repository.PointerFileEntryRepository.CreatePointerFileEntryResult.InsertedDeleted:
                logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Inserted 'deleted' entry.");
                break;
            case Repository.PointerFileEntryRepository.CreatePointerFileEntryResult.NoChange:
                logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. No change made, latest entry was up to date.");
                break;
            default:
                throw new NotImplementedException();
        }
    }
}


internal class DeleteBinaryFilesBlock : ChannelTaskBlockBase<BinaryFile>
{
    public DeleteBinaryFilesBlock(ILoggerFactory loggerFactory,
        Func<ChannelReader<BinaryFile>> sourceFunc,
        int maxDegreeOfParallelism,
        Action onCompleted) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, onCompleted: onCompleted)
    {
    }

    protected override Task ForEachBodyImplAsync(BinaryFile bf, CancellationToken ct)
    {
        logger.LogInformation($"RemoveLocal flag is set - Deleting binary '{bf.RelativeName}'...");
        bf.Delete();
        logger.LogInformation($"RemoveLocal flag is set - Deleting binary '{bf.RelativeName}'... done");

        return Task.CompletedTask;
    }
}


internal class CreateDeletedPointerFileEntryForDeletedPointerFilesBlock : ChannelTaskBlockBase<PointerFileEntry>
{
    public CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(ILoggerFactory loggerFactory,
        Func<Task<ChannelReader<PointerFileEntry>>> sourceFunc,
        int maxDegreeOfParallelism,
        Repository repo,
        DirectoryInfo root,
        PointerService pointerService,
        DateTime versionUtc,
        Action onCompleted) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, onCompleted: onCompleted)
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

    protected override async Task ForEachBodyImplAsync(PointerFileEntry pfe, CancellationToken ct)
    {
        if (!pfe.IsDeleted &&
            pointerService.GetPointerFile(root, pfe) is null &&
            pointerService.GetBinaryFile(root, pfe, ensureCorrectHash: false) is null) //PointerFileEntry is marked as exists and there is no PointerFile and there is no BinaryFile (only on PointerFile may not work since it may still be in the pipeline to be created)
        {
            logger.LogInformation($"The pointer or binary for '{pfe.RelativeName}' no longer exists locally, marking entry as deleted");
            await repo.PointerFileEntries.CreateDeletedPointerFileEntryAsync(pfe, versionUtc);
        }
    }
}


//internal class ExportToJsonBlock : TaskBlockBase<ChannelReader<PointerFileEntry>> //! must be single threaded hence TaskBlockBase
//{
//    public ExportToJsonBlock(ILoggerFactory loggerFactory,
//        Func<Task<ChannelReader<PointerFileEntry>>> sourceFunc,
//        Repository repo,
//        DateTime versionUtc,
//        Action done) : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, done: done)
//    {
//        this.repo = repo;
//        this.versionUtc = versionUtc;
//    }

//    private readonly Repository repo;
//    private readonly DateTime versionUtc;


//    protected override async Task TaskBodyImplAsync(ChannelReader<PointerFileEntry> source)
//    {
//        logger.LogInformation($"Writing state to JSON...");

//        using var file = File.Create($"arius-state-{versionUtc.ToLocalTime():yyyyMMdd-HHmmss}.json");
//        var writer = new Utf8JsonWriter(file, new JsonWriterOptions() { Indented = true });
//        writer.WriteStartArray();

//        // See https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-converters-how-to

//        await foreach (var pfe in source.ReadAllAsync()
//                     //.AsParallel().WithDegreeOfParallelism(8) // ! Cannot write to file concurrently
//                 )
//        {
//            var chs = await repo.BinaryManifests.GetChunkHashesAsync(pfe.BinaryHash);
//            var entry = new PointerFileEntryWithChunkHashes(pfe, chs);

//            JsonSerializer.Serialize(writer, entry, new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default });
//        }

//        writer.WriteEndArray();
//        await writer.FlushAsync();

//        logger.LogInformation($"Writing state to JSON... done");
//    }

//    private readonly struct PointerFileEntryWithChunkHashes
//    {
//        public PointerFileEntryWithChunkHashes(PointerFileEntry pfe, ChunkHash[] chs)
//        {
//            this.pfe = pfe;
//            this.chs = chs;
//        }

//        private readonly PointerFileEntry pfe;
//        private readonly ChunkHash[] chs;

//        public string BinaryHash => pfe.BinaryHash.Value;
//        public IEnumerable<string> ChunkHashes => chs.Select(h => h.Value);
//        public string RelativeName => pfe.RelativeName;
//        public DateTime VersionUtc => pfe.VersionUtc;
//        public bool IsDeleted => pfe.IsDeleted;
//        public DateTime? CreationTimeUtc => pfe.CreationTimeUtc;
//        public DateTime? LastWriteTimeUtc => pfe.LastWriteTimeUtc;
//    }
//}


internal class ValidateBlock
{
    public ValidateBlock(ILoggerFactory loggerFactory,
        Func<BlockingCollection<PointerFileEntry>> sourceFunc,
        Repository repo,
        DateTime versionUtc,
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