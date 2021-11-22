using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ConcurrentCollections;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Arius.Core.Commands.Archive;

internal partial class ArchiveCommand
{
    private class IndexBlock : TaskBlockBase<DirectoryInfo>
    {
        public IndexBlock(ArchiveCommand command,
            Func<DirectoryInfo> sourceFunc,
            int maxDegreeOfParallelism,
            TaskCompletionSource binaryFileUploadCompletedTaskCompletionSource,
            Func<PointerFile, Task> onIndexedPointerFile,
            Func<(BinaryFile BinaryFile, bool AlreadyBackedUp), Task> onIndexedBinaryFile,
            Action onBinaryFileIndexCompleted,
            Action onCompleted)
            : base(loggerFactory: command.executionServices.GetRequiredService<ILoggerFactory>(), 
                sourceFunc: sourceFunc, 
                onCompleted: onCompleted)
        {
            this.stats = command.stats;
            this.fastHash = command.executionServices.Options.FastHash;
            this.pointerService = command.executionServices.GetRequiredService<PointerService>();
            this.repo = command.executionServices.GetRequiredService<Repository>();
            this.hvp = command.executionServices.GetRequiredService<IHashValueProvider>();

            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.binaryFileUploadCompletedTaskCompletionSource = binaryFileUploadCompletedTaskCompletionSource;
            this.onIndexedPointerFile = onIndexedPointerFile;
            this.onIndexedBinaryFile = onIndexedBinaryFile;
            this.onBinaryFileIndexCompleted = onBinaryFileIndexCompleted;
        }

        private readonly ArchiveCommandStatistics stats;
        private readonly bool fastHash;
        private readonly PointerService pointerService;
        private readonly Repository repo;
        private readonly IHashValueProvider hvp;
        private readonly int maxDegreeOfParallelism;
        private readonly TaskCompletionSource binaryFileUploadCompletedTaskCompletionSource;
        private readonly Func<PointerFile, Task> onIndexedPointerFile;
        private readonly Func<(BinaryFile BinaryFile, bool AlreadyBackedUp), Task> onIndexedBinaryFile;
        private readonly Action onBinaryFileIndexCompleted;

        protected override async Task TaskBodyImplAsync(DirectoryInfo root)
        {
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
                            stats.AddLocalRepositoryStatistic(beforePointerFiles: 1);

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

                            logger.LogInformation($"Found BinaryFile '{bf.RelativeName}'");
                            stats.AddLocalRepositoryStatistic(beforeFiles: 1, beforeSize: bf.Length);

                            if (pf is not null)
                            {
                                if (pf.Hash != bh)
                                    throw new InvalidOperationException($"The PointerFile '{pf.FullName}' is not valid for the BinaryFile '{bf.FullName}' (BinaryHash does not match). Has the BinaryFile been updated? Delete the PointerFile and try again.");

                                if (!await repo.Binaries.ExistsAsync(bh)) //TODO this is a choke point for large state files -- the hashing could already go ahead?
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

    private class UploadBinaryFileBlock : ChannelTaskBlockBase<BinaryFile>
    {
        public UploadBinaryFileBlock(ArchiveCommand command,
            Func<ChannelReader<BinaryFile>> sourceFunc,
            int maxDegreeOfParallelism,
            Func<BinaryFile, Task> onBinaryExists,
            Action onCompleted)
                : base(loggerFactory: command.executionServices.GetRequiredService<ILoggerFactory>(),
                    sourceFunc: sourceFunc, 
                    maxDegreeOfParallelism: maxDegreeOfParallelism, 
                    onCompleted: onCompleted)
        {
            this.stats = command.stats;
            this.repo = command.executionServices.GetRequiredService<Repository>();
            this.options = command.executionServices.Options;

            this.onBinaryExists = onBinaryExists;
        }

        private readonly ArchiveCommandStatistics stats;
        private readonly Repository repo;
        private readonly IArchiveCommandOptions options;

        private readonly Func<BinaryFile, Task> onBinaryExists;

        private readonly ConcurrentDictionary<BinaryHash, Task<bool>> remoteBinaries = new();
        private readonly ConcurrentDictionary<BinaryHash, TaskCompletionSource> uploadingBinaries = new();


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

            stats.AddLocalRepositoryStatistic(deltaFiles: 1, deltaSize: bf.Length);

            // [Concurrently] Build a local cache of the remote binaries -- ensure we call BinaryExistsAsync only once
            var binaryExistsRemote = await remoteBinaries.GetOrAdd(bf.Hash, async (_) => await repo.Binaries.ExistsAsync(bf.Hash)); //TODO since this is now backed by a database, we do not need to cache this locally?
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

                    var bp = await repo.Binaries.UploadAsync(bf, options);

                    stats.AddRemoteRepositoryStatistic(deltaBinaries: 1, deltaSize: bp.IncrementalLength);

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

            await onBinaryExists(bf);
        }
    }


    internal class CreatePointerFileIfNotExistsBlock : ChannelTaskBlockBase<BinaryFile>
    {
        public CreatePointerFileIfNotExistsBlock(ArchiveCommand command,
            Func<ChannelReader<BinaryFile>> sourceFunc,
            int maxDegreeOfParallelism,
            Func<BinaryFile, Task> onSuccesfullyBackedUp,
            Func<PointerFile, Task> onPointerFileCreated,
            Action onCompleted)
                : base(loggerFactory: command.executionServices.GetRequiredService<ILoggerFactory>(),
                    sourceFunc: sourceFunc, 
                    maxDegreeOfParallelism: 
                    maxDegreeOfParallelism, 
                    onCompleted: onCompleted)
        {
            this.stats = command.stats;
            this.pointerService = command.executionServices.GetRequiredService<PointerService>();
            this.onSuccesfullyBackedUp = onSuccesfullyBackedUp;
            this.onPointerFileCreated = onPointerFileCreated;
        }

        private readonly ArchiveCommandStatistics stats;
        private readonly PointerService pointerService;
        private readonly Func<BinaryFile, Task> onSuccesfullyBackedUp;
        private readonly Func<PointerFile, Task> onPointerFileCreated;

        protected override async Task ForEachBodyImplAsync(BinaryFile bf, CancellationToken ct)
        {
            logger.LogDebug($"Creating pointer for '{bf.RelativeName}'...");

            var (created, pf) = pointerService.CreatePointerFileIfNotExists(bf);

            logger.LogInformation($"Creating pointer for '{bf.RelativeName}'... done");
            if (created)
                stats.AddLocalRepositoryStatistic(deltaPointerFiles: 1);

            await onSuccesfullyBackedUp(bf);
            await onPointerFileCreated(pf);
        }
    }


    internal class CreatePointerFileEntryIfNotExistsBlock : ChannelTaskBlockBase<PointerFile>
    {
        public CreatePointerFileEntryIfNotExistsBlock(ArchiveCommand command,
            Func<ChannelReader<PointerFile>> sourceFunc,
            int maxDegreeOfParallelism,
            DateTime versionUtc,
            Action onCompleted) 
                : base(loggerFactory: command.executionServices.GetRequiredService<ILoggerFactory>(),
                    sourceFunc: sourceFunc, 
                    maxDegreeOfParallelism: maxDegreeOfParallelism, 
                    onCompleted: onCompleted)
        {
            this.stats = command.stats;
            this.repo = command.executionServices.GetRequiredService<Repository>();
            this.versionUtc = versionUtc;
        }

        private readonly ArchiveCommandStatistics stats;
        private readonly Repository repo;
        private readonly DateTime versionUtc;

        protected override async Task ForEachBodyImplAsync(PointerFile pointerFile, CancellationToken ct)
        {
            logger.LogDebug($"Upserting PointerFile entry for '{pointerFile.RelativeName}'...");

            var r = await repo.PointerFileEntries.CreatePointerFileEntryIfNotExistsAsync(pointerFile, versionUtc);

            switch (r)
            {
                case Repository.PointerFileEntryRepository.CreatePointerFileEntryResult.Inserted:
                    logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Inserted entry.");
                    stats.AddRemoteRepositoryStatistic(deltaPointerFileEntries: 1);
                    break;
                case Repository.PointerFileEntryRepository.CreatePointerFileEntryResult.InsertedDeleted:
                    logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Inserted 'deleted' entry.");
                    stats.AddRemoteRepositoryStatistic(deltaPointerFileEntries: -1);
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
        public DeleteBinaryFilesBlock(ArchiveCommand command,
            Func<ChannelReader<BinaryFile>> sourceFunc,
            int maxDegreeOfParallelism,
            Action onCompleted)
                : base(loggerFactory: command.executionServices.GetRequiredService<ILoggerFactory>(), 
                    sourceFunc: sourceFunc, 
                    maxDegreeOfParallelism: maxDegreeOfParallelism, 
                    onCompleted: onCompleted)
        {
            this.stats = command.stats;
        }

        private readonly ArchiveCommandStatistics stats;

        protected override Task ForEachBodyImplAsync(BinaryFile bf, CancellationToken ct)
        {

            if (File.Exists(bf.FullName))
            {
                logger.LogDebug($"RemoveLocal flag is set - Deleting binary '{bf.RelativeName}'...");

                bf.Delete();

                logger.LogInformation($"RemoveLocal flag is set - Deleting binary '{bf.RelativeName}'... done");
                stats.AddLocalRepositoryStatistic(deltaFiles: -1, deltaSize: bf.Length * -1); //TODO test icw 
            }

            return Task.CompletedTask;
        }
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

internal class UpdateTierBlock : TaskBlockBase
{
    public UpdateTierBlock(ILoggerFactory loggerFactory,
        Func<Repository> sourceFunc,
        int maxDegreeOfParallelism,
        Repository repo,
        AccessTier targetAccessTier,
        Action onCompleted) : base(loggerFactory: loggerFactory, onCompleted: onCompleted)
    {
        this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        this.repo = repo;
        this.targetAccessTier = targetAccessTier;
    }

    private readonly int maxDegreeOfParallelism;
    private readonly Repository repo;
    private readonly AccessTier targetAccessTier;

    protected override async Task TaskBodyImplAsync()
    {
        if (targetAccessTier != AccessTier.Archive)
            return; //only support mass moving to Archive tier to avoid huge excess costs when rehydrating the entire archive

        await repo.Chunks.SetAllAccessTierAsync(targetAccessTier, maxDegreeOfParallelism);
    }
}