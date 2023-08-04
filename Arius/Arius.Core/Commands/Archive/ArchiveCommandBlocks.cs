﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
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
            IArchiveCommandOptions options,
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

                            if (await repo.Binaries.ExistsAsync(pf.BinaryHash))
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

                                if (!await repo.Binaries.ExistsAsync(bf.BinaryHash)) //TODO this is a choke point for large state files -- the hashing could already go ahead?
                                {
                                    logger.LogWarning($"BinaryFile {bf} has a PointerFile that points to a nonexisting (remote) Binary ('{bf.BinaryHash.ToShortString()}'). Uploading binary again.");
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

            IArchiveCommandOptions options,
            Func<BinaryFile, Task> onBinaryExists)
                : base(command.loggerFactory, sourceFunc, maxDegreeOfParallelism, onCompleted)
        {
            this.stats          = command.stats;
            this.repo           = command.repo;
            this.options        = options;
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

            if (options.RemoveLocal) // NOTE THIS BLOCK CAN BE DELETED IF THE ARCHIVE STATISTICS FUNCIONALITY WORKS FINE
                stats.AddLocalRepositoryStatistic(deltaFiles: 0, deltaSize: 0); //Do not add -1 it here, it is set in DeleteBinaryFilesBlock after successful deletion
            else
                stats.AddLocalRepositoryStatistic(deltaFiles: 0, deltaSize: 0); //if we're keeping the local binaries, there are no deltas due to the archive operation

            // [Concurrently] Build a local cache of the remote binaries -- ensure we call BinaryExistsAsync only once
            var binaryExistsRemote = await remoteBinaries.GetOrAdd(bf.BinaryHash, async (_) => await repo.Binaries.ExistsAsync(bf.BinaryHash)); //TODO since this is now backed by a database, we do not need to cache this locally?
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

                    var bp = await repo.Binaries.UploadAsync(bf, options);

                    stats.AddRemoteRepositoryStatistic(deltaBinaries: 1, deltaSize: bp.IncrementalLength);

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

            IArchiveCommandOptions options) 
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

            var r = await repo.PointerFileEntries.CreatePointerFileEntryIfNotExistsAsync(pointerFile, versionUtc);

            switch (r)
            {
                case Repository.PointerFileEntryRepository.CreatePointerFileEntryResult.Inserted:
                    logger.LogInformation($"Upserting PointerFile entry for {pointerFile}... done. Inserted entry.");
                    stats.AddRemoteRepositoryStatistic(deltaPointerFileEntries: 1);
                    break;
                case Repository.PointerFileEntryRepository.CreatePointerFileEntryResult.InsertedDeleted:
                    // TODO IS THIS EVER HIT?? I dont think so - the deleted entry is created in CreateDeletedPointerFileEntryForDeletedPointerFilesBlock
                    logger.LogInformation($"Upserting PointerFile entry for {pointerFile}... done. Inserted 'deleted' entry.");
                    stats.AddRemoteRepositoryStatistic(deltaPointerFileEntries: 1); //note this is PLUS 1 since we re adding an entry really
                    break;
                case Repository.PointerFileEntryRepository.CreatePointerFileEntryResult.NoChange:
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

            IArchiveCommandOptions options,
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

                await repo.PointerFileEntries.CreateDeletedPointerFileEntryAsync(pfe, versionUtc);
            }
        }
    }


    private class UpdateTierBlock : TaskBlockBase
    {
        public UpdateTierBlock(ArchiveCommand command,
            Func<Repository> sourceFunc,
            Action onCompleted,

            int maxDegreeOfParallelism,
            IArchiveCommandOptions options) 
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
            // TODO to Cold tier

            if (targetAccessTier != AccessTier.Archive)
                return; //only support mass moving to Archive tier to avoid huge excess costs when rehydrating the entire archive

            var blobsNotInTier = repo.Chunks.GetAllChunkBlobs().Where(cbb => cbb.AccessTier != targetAccessTier);

            await Parallel.ForEachAsync(blobsNotInTier,
                new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                async (cbb, ct) =>
                {
                    var updated = await cbb.SetAccessTierPerPolicyAsync(targetAccessTier);
                    if (updated)
                        logger.LogInformation($"Set acces tier to '{targetAccessTier.ToString()}' for chunk '{cbb.ChunkHash.ToShortString()}'");
                });
        }
    }
}