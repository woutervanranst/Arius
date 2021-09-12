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
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Arius.Core.Commands.Archive
{
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
                var rn = fi.GetRelativeName(root);

                if (fi.IsPointerFile())
                {
                    //PointerFile
                    logger.LogInformation($"Found PointerFile '{rn}'");

                    var pf = new PointerFile(root, fi);

                    indexedPointerFile(pf);
                }
                else
                {
                    //BinaryFile
                    logger.LogInformation($"Found BinaryFile '{rn}'");

                    //Get the Hash for this file
                    ManifestHash manifestHash;
                    var pf = pointerService.GetPointerFile(root, fi);
                    if (fastHash && pf is not null)
                    {
                        //A corresponding PointerFile exists
                        logger.LogDebug($"Using fasthash for '{rn}'");
                        manifestHash = pf.Hash;
                    }
                    else
                    {
                        manifestHash = hvp.GetManifestHash(fi);
                    }

                    logger.LogInformation($"Hashing BinaryFile '{rn}'... done. Hash: '{manifestHash.ToShortString()}'");


                    var bf = new BinaryFile(root, fi, manifestHash);


                    if (pf is not null && pf.Hash == manifestHash)
                    {
                        //An equivalent PointerFile already exists and is already being sent through the pipe - skip.

                        if (!await repo.ManifestExistsAsync(manifestHash))
                            throw new InvalidOperationException($"BinaryFile '{bf.RelativeName}' has a PointerFile that points to a manifest ('{manifestHash.ToShortString()}') that no longer exists.");

                        logger.LogInformation($"BinaryFile '{bf.RelativeName}' already has a PointerFile that is being processed. Skipping BinaryFile.");
                        indexedBinaryFile((bf, AlreadyBackedUp: true));
                    }
                    else
                    {
                        // To process
                        indexedBinaryFile((bf, AlreadyBackedUp: false));
                    }
                }
            }
        }
    }


    internal class ChunkBinaryFileBlock : BlockingCollectionTaskBlockBase<BinaryFile>
    {
        public ChunkBinaryFileBlock(
            ILoggerFactory loggerFactory,
            Func<BlockingCollection<BinaryFile>> sourceFunc,
            int degreeOfParallelism,
            Chunker chunker,
            Repository repo,
            ArchiveCommandOptions options,
            
            Func<(IChunk, TaskCompletionSource<long>), Task> enqueueChunkForUpload,
            Action<BinaryFile> binaryExists,
            Action done) : base(loggerFactory, sourceFunc, degreeOfParallelism, done)
        {
            this.chunker = chunker;
            this.repo = repo;
            this.options = options;
            this.enqueueChunkForUpload = enqueueChunkForUpload;
            this.binaryExists = binaryExists;
        }

        private readonly Chunker chunker;
        private readonly Repository repo;
        private readonly ArchiveCommandOptions options;
        private readonly Func<(IChunk, TaskCompletionSource<long>), Task> enqueueChunkForUpload;
        private readonly Action<BinaryFile> binaryExists;

        private readonly ConcurrentDictionary<ManifestHash, Task<bool>> remoteManifests = new();
        private readonly ConcurrentDictionary<ManifestHash, TaskCompletionSource> uploadingBinaries = new();
        

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

            // [Concurrently] Build a local cache of the remote manifests -- ensure we call ManifestExistsAsync only once
            var manifestExistsRemote = await remoteManifests.GetOrAdd(bf.Hash, async (_) => await repo.ManifestExistsAsync(bf.Hash));
            if (manifestExistsRemote)
            {
                // 1 Exists remote
                logger.LogInformation($"Binary for '{bf.Name}' ('{bf.Hash.ToShortString()}') already exists. No need to upload.");
            }
            else
            {
                // 2 Did not exist remote before the run -- ensure we start the upload only once

                /* TryAdd returns true if the new value was added
                 * ALWAYS create a new TaskCompletionSource with the RunContinuationsAsynchronously option, otherwise the continuations will run on THIS thread -- https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md#always-create-taskcompletionsourcet-with-taskcreationoptionsruncontinuationsasynchronously
                 */
                var toUpload = uploadingBinaries.TryAdd(bf.Hash, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)); 
                if (toUpload)
                {
                    // 2.1 Does not yet exist remote and not yet being created --> upload
                    logger.LogInformation($"Binary for '{bf.Name}' ('{bf.Hash.ToShortString()}') does not exist remotely. To upload and create pointer.");

                    await UploadBinaryAsync(bf);

                    uploadingBinaries[bf.Hash].SetResult();
                }
                else
                {
                    // upload already ongoing
                    var t = uploadingBinaries[bf.Hash].Task;
                    if (!t.IsCompleted)
                    {
                        // 2.2 Did not exist remote but is being created
                        logger.LogInformation($"Binary for '{bf.Name}' ('{bf.Hash.ToShortString()}') does not exist remotely but is being uploaded. Wait for upload to finish.");

                        await t;
                    }
                    else
                    {
                        // 2.3  Did not exist remote but is created in the mean time
                        logger.LogInformation($"Binary for '{bf.Name}' ('{bf.Hash.ToShortString()}') did not exist remotely but was uploaded in the mean time.");
                    }
                }
            }
            
            binaryExists(bf);
        }

        private async Task UploadBinaryAsync(BinaryFile bf)
        {
            logger.LogInformation($"Uploading {bf.Length.GetBytesReadable()} of '{bf.Name}' ('{bf.Hash.ToShortString()}')...");
            var sw = new Stopwatch();
            sw.Start();

            var (chs, length) = options.Dedup switch
            {
                true => await UploadChunkedBinaryAsync(bf),
                false => await UploadBinaryChunkAsync(bf)
            };
            
            sw.Stop();

            var megabytepersecond = Math.Round(bf.Length / (1024 * 1024 * (double)sw.ElapsedMilliseconds / 1000), 3);
            var megabitpersecond = Math.Round(bf.Length * 8 / (1024 * 1024 * (double)sw.ElapsedMilliseconds / 1000), 3);

            logger.LogInformation($"Uploading {bf.Length.GetBytesReadable()} of '{bf.Name}' ('{bf.Hash.ToShortString()}')... Completed in {sw.ElapsedMilliseconds / 1000}s ({megabytepersecond} MBps / {megabitpersecond} Mbps)");

            // Create the Manifest
            await repo.CreateManifestAsync(bf.Hash, chs);

            // Create the ManifestPropertyEntry
            await repo.CreateManifestPropertyAsync(bf, length, chs.Length);
        }

        
        /// <summary>
        /// Chunk the BinaryFile then upload all the chunks in parallel
        /// </summary>
        /// <param name="bf"></param>
        /// <returns></returns>
        private async Task<(ChunkHash[], long length)> UploadChunkedBinaryAsync(BinaryFile bf)
        {
            var chs = new Dictionary<ChunkHash, Task<long>>(); //ChunkHashes for this BinaryFile

            var sw = new Stopwatch();
            sw.Start();

            using (var bfs = await bf.OpenReadAsync())
            {
                foreach (var chunk in chunker.Chunk(bfs))
                {
                    var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                    await enqueueChunkForUpload((chunk, tcs));
                    chs.Add(chunk.Hash, tcs.Task);
                }
            }

            sw.Stop();
            var mbps = Math.Round(bf.Length / (1024 * 1024 * (double)sw.Elapsed.TotalSeconds), 3);
            logger.LogInformation($"Completed chunking of '{bf.Name}' in {chs.Count} chunks in {sw.ElapsedMilliseconds / 1000}s at {mbps} MBps");

            var totalLength = (await Task.WhenAll(chs.Values)).Sum();

            return (chs.Keys.ToArray(), totalLength);
        }

        /// <summary>
        /// Upload one single BinaryFile
        /// </summary>
        /// <param name="bf"></param>
        /// <returns></returns>
        private async Task<(ChunkHash[], long length)> UploadBinaryChunkAsync(BinaryFile bf)
        {
            var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);

            await enqueueChunkForUpload((bf, tcs));

            var length = await tcs.Task;

            return (((IChunk)bf).Hash.SingleToArray(), length);

            //var length = await repo.UploadChunkAsync(bf, options.Tier);

            //return (((IChunk)bf).Hash.SingleToArray(), length);
        }
    }


    internal class UploadBlock : ChannelTaskBlockBase<(IChunk, TaskCompletionSource<long>)>
    {
        public UploadBlock(
            ILoggerFactory loggerFactory,
            Func<Channel<(IChunk, TaskCompletionSource<long>)>> sourceFunc,
            int degreeOfParallelism,
            Repository repo,
            ArchiveCommandOptions options,
            Action done) : base(loggerFactory, sourceFunc, degreeOfParallelism, done )
        {
            this.repo = repo;
            this.options = options;
        }

        private readonly Repository repo;
        private readonly ArchiveCommandOptions options;

        private int parallelism = 0;
        private readonly ConcurrentDictionary<ChunkHash, Task<bool>> remoteChunks = new();
        private readonly ConcurrentDictionary<ChunkHash, TaskCompletionSource<long>> uploadingChunks = new();

        protected override async Task ForEachBodyImplAsync((IChunk, TaskCompletionSource<long>) item, CancellationToken ct)
        {
            Interlocked.Add(ref parallelism, 1);
            logger.LogInformation($"{parallelism}"); // - queue depth: {source.Reader.Count}");

            var (chunk, tcs) = item;

            var chunkExistsRemote = await remoteChunks.GetOrAdd(chunk.Hash, async (_) => await repo.ChunkExistsAsync(chunk.Hash));
            if (chunkExistsRemote)
            {
                // 1 Exists remote
                logger.LogInformation($"Chunk with hash '{chunk.Hash.ToShortString()}' already exists. No need to upload.");

                throw new NotImplementedException();
                tcs.SetResult(0);
            }
            else
            { 
                bool toUpload = uploadingChunks.TryAdd(chunk.Hash, tcs);
                if (toUpload)
                {
                    // 2 Does not yet exist remote and not yet being created --> upload
                    logger.LogInformation($"Chunk with hash '{chunk.Hash.ToShortString()}' does not exist remotely. To upload.");

                    var length = await repo.UploadChunkAsync(chunk, options.Tier);

                    uploadingChunks[chunk.Hash].SetResult(length);
                }
                else
                {
                    // 3 Does not exist remote but is being created
                    logger.LogInformation($"Chunk with hash '{chunk.Hash.ToShortString()}' does not exist remotely but is already being uploaded. Wait for its creation.");

                    //TODO TES THIS PATH

                    await uploadingChunks[chunk.Hash].Task;
                }
            }

            Interlocked.Add(ref parallelism, -1);
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
                case Repository.CreatePointerFileEntryResult.Upserted:
                    logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Upserted entry.");
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

            using Stream file = File.Create($"arius-state-{versionUtc.ToLocalTime():yyyyMMdd-HHmmss}.json");
            var writer = new Utf8JsonWriter(file, new JsonWriterOptions() { Indented = true });
            writer.WriteStartArray();

            // See https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-converters-how-to

            foreach (var pfe in source
                    //.AsParallel().WithDegreeOfParallelism(8)
                    //.AsEnumerable()) 
                    .GetConsumingEnumerable())
            {
                var chs = await repo.GetChunkHashesForManifestAsync(pfe.ManifestHash);
                var entry = new PointerFileEntryWithChunkHashes(pfe, chs);

                //lock (writer)
                //{ 
                JsonSerializer.Serialize(writer, entry /*entry*/, new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default });
                //}
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

            public string ManifestHash => pfe.ManifestHash.Value;
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
}