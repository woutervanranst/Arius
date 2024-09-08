using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Services;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using FluentValidation;
using Humanizer.Bytes;
using MediatR;
using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using File = Arius.Core.Domain.Storage.FileSystem.File;

namespace Arius.Core.New.Commands.Archive;

public abstract record ArchiveCommandNotification(ArchiveCommand Command) : INotification;
public record FilePairFoundNotification(ArchiveCommand Command, FilePair FilePair) : ArchiveCommandNotification(Command);
public record FilePairHashingStartedNotification(ArchiveCommand Command, FilePair FilePair) : ArchiveCommandNotification(Command);
public record FilePairHashingCompletedNotification(ArchiveCommand Command, FilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record UploadBinaryFileStartedNotification(ArchiveCommand Command) : ArchiveCommandNotification(Command);
public record UploadBinaryFileCompletedNotification(ArchiveCommand Command, long OriginalLength, long ArchivedLength, double uploadSpeedMBps) : ArchiveCommandNotification(Command);
public record CreatedPointerFileNotification(ArchiveCommand Command, IFileWithHash PointerFile) : ArchiveCommandNotification(Command);
public record ArchiveCommandDoneNotification(ArchiveCommand Command) : ArchiveCommandNotification(Command);


internal class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{
    private readonly IMediator                      mediator;
    private readonly IFileSystem                    fileSystem;
    private readonly IStateDbRepositoryFactory      stateDbRepositoryFactory;
    private readonly IStorageAccountFactory         storageAccountFactory;
    private readonly ILogger<ArchiveCommandHandler> logger;

    public ArchiveCommandHandler(
        IMediator mediator,
        IFileSystem fileSystem,
        IStateDbRepositoryFactory stateDbRepositoryFactory,
        IStorageAccountFactory storageAccountFactory,
        ILogger<ArchiveCommandHandler> logger)
    {
        this.mediator                 = mediator;
        this.fileSystem               = fileSystem;
        this.stateDbRepositoryFactory = stateDbRepositoryFactory;
        this.storageAccountFactory    = storageAccountFactory;
        this.logger                   = logger;
    }

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        await new ArchiveCommandValidator().ValidateAndThrowAsync(request, cancellationToken);

        // Start download of the latest state database
        var stateDbRepositoryTask = Task.Run(async () => await stateDbRepositoryFactory.CreateAsync(request.Repository), cancellationToken);


        // 1. Index the request.LocalRoot
        var filesToHash = GetBoundedChannel<FilePair>(request.FilesToHash_BufferSize, true);
        var indexTask = Task.Run(async () =>
        {
            foreach (var fp in fileSystem.EnumerateFilePairs(request.LocalRoot))
            {
                //await Task.Delay(2000);
                
                await mediator.Publish(new FilePairFoundNotification(request, fp), cancellationToken);
                logger.LogInformation("Found {fp}", fp);
                await filesToHash.Writer.WriteAsync(fp, cancellationToken); // A10
            }

            filesToHash.Writer.Complete(); // C10
        }, cancellationToken);


        // 2. Hash the filepairs
        var hashedFilePairs = GetBoundedChannel<FilePairWithHash>(request.BinariesToUpload_BufferSize, false);
        var hvp              = new SHA256Hasher(request.Repository.Passphrase);
        var hashTask = Parallel.ForEachAsync(
            filesToHash.Reader.ReadAllAsync(cancellationToken),
            GetParallelOptions(request.Hash_Parallelism),
            async (filePair, ct) =>
            {
                await mediator.Publish(new FilePairHashingStartedNotification(request, filePair), ct);
                //await Task.Delay(2000);
                var filePairWithHash = await HashFilesAsync(request.FastHash, hvp, filePair);
                await mediator.Publish(new FilePairHashingCompletedNotification(request, filePairWithHash), ct);
                
                await hashedFilePairs.Writer.WriteAsync(filePairWithHash, ct); // 20
            });

        hashTask.ContinueWith(_ => hashedFilePairs.Writer.Complete(), cancellationToken); // C20

        var stateDbRepository = await stateDbRepositoryTask;


        // 3. Decide whether binaries need uploading
        var binariesToUpload           = GetBoundedChannel<BinaryFileWithHash>(request.BinariesToUpload_BufferSize, false);
        var pointerFileEntriesToCreate = GetBoundedChannel<BinaryFileWithHash>(request.PointerFileEntriesToCreate_BufferSize, false);
        var latentPointers             = new ConcurrentBag<PointerFileWithHash>();

        var uploadingBinaries = new Dictionary<Hash, TaskCompletionSource>();
        var addUploadedBinariesToPointerFileQueueTasks = new ConcurrentBag<Task>();

        var uploadRouterTask = Task.Run(async () =>
        {
            await foreach (var pwh in hashedFilePairs.Reader.ReadAllAsync(cancellationToken))
            {
                if (pwh.HasBinaryFile)
                {
                    // The binary exists locally
                    var r = DetermineUploadStatus(pwh.Hash);

                    if (pwh.HasExistingPointerFile && r is not UploadStatus.Uploaded)
                    {
                        // edge case: the PointerFile already exists but the binary is not uploaded (yet) -- eg when re-uploading an entire archive -> check them later
                        latentPointers.Add(pwh.PointerFile!); // A31
                    }

                    switch (r)
                    {
                        case UploadStatus.NotStarted:
                            // 2.1 Does not yet exist remote and not yet being uploaded --> upload
                            logger.LogInformation("Binary for {relativeName} does not exist remotely. Starting upload.", pwh.RelativeName);

                            await binariesToUpload.Writer.WriteAsync(pwh.BinaryFile!, cancellationToken); // A32

                            //stats.AddRemoteRepositoryStatistic(deltaBinaries: 1, deltaSize: ce.IncrementalLength);
                            break;
                        case UploadStatus.Uploading:
                            // 2.2 Does not yet exist remote but is already being uploaded
                            logger.LogInformation("Binary for {relativeName} does not exist remotely but is already being uploaded.", pwh.RelativeName);

                            addUploadedBinariesToPointerFileQueueTasks.Add(uploadingBinaries[pwh.Hash].Task.ContinueWith(async _ =>
                            {
                                logger.LogInformation("Binary for {relativeName} has been uploaded.", pwh.RelativeName);

                                await pointerFileEntriesToCreate.Writer.WriteAsync(pwh.BinaryFile!, cancellationToken); // A332
                            }, cancellationToken)); // A331

                            break;
                        case UploadStatus.Uploaded:
                            // 2.3 Is already uploaded
                            logger.LogInformation("Binary for {relativeName} already exists remotely.", pwh.RelativeName);

                            await pointerFileEntriesToCreate.Writer.WriteAsync(pwh.BinaryFile!, cancellationToken); // A35

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            binariesToUpload.Writer.Complete(); // C30
        }, cancellationToken);
        

        // 4. Upload the binaries
        var repository = storageAccountFactory.GetRepository(request.Repository);
        var uploadBinariesTask = Parallel.ForEachAsync(
            binariesToUpload.Reader.ReadAllAsync(cancellationToken),
            GetParallelOptions(request.UploadBinaryFileBlock_BinaryFileParallelism),
            async (bfwh, ct) =>
            {
                await mediator.Publish(new UploadBinaryFileStartedNotification(request), ct);

                var stopwatch = Stopwatch.StartNew();
                var bp = await repository.UploadBinaryFileAsync(bfwh, s => GetEffectiveStorageTier(request.StorageTiering, request.Tier, s), ct);
                stopwatch.Stop();
                
                var elapsedTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                var uploadSpeedMbps = ByteSize.FromBytes(bp.ArchivedLength).Megabytes / elapsedTimeInSeconds;

                await mediator.Publish(new UploadBinaryFileCompletedNotification(request, bp.OriginalLength, bp.ArchivedLength, uploadSpeedMbps), ct);

                logger.LogInformation("Uploaded {hash} ({binaryFile}) in {elapsedTimeInSeconds} seconds @ {speed:F2} MBps", bfwh.Hash, bfwh.RelativeName, elapsedTimeInSeconds, uploadSpeedMbps);

                HasBeenUploaded(bp); // A41

                await pointerFileEntriesToCreate.Writer.WriteAsync(bfwh, ct); // A40
            }
        );

        Task.WhenAll(uploadBinariesTask, uploadRouterTask)
            .ContinueWith(async _ =>
            {
                //await uploadRouterTask;
                await addUploadedBinariesToPointerFileQueueTasks.WhenAll();
                pointerFileEntriesToCreate.Writer.Complete();
            }, cancellationToken); // C40


        //5. Now that all binaries are uploaded, check the 'stale' pointers (pointers that were present but did not have a remote binary)
        var latentPointerTask = Task.Run(async () =>
        {
            await uploadBinariesTask; // C41 

            foreach (var pf in latentPointers)
            {
                if (!stateDbRepository.BinaryExists(pf.Hash))
                    throw new InvalidOperationException($"PointerFile {pf.RelativeName} exists on disk but no corresponding binary exists either locally or remotely.");
            }
        }, cancellationToken);


        // 6. Create PointerFileEntries and PointerFiles
        var binaryFilesToDelete = GetBoundedChannel<BinaryFileWithHash>(request.BinariesToDelete_BufferSize, true);
        var pointerFileCreationTask = Task.Run(async () =>
        {
            await foreach (var bfwh in pointerFileEntriesToCreate.Reader.ReadAllAsync(cancellationToken))
            {
                // 1. Create the PointerFile
                var pfwh = PointerFileWithHash.Create(bfwh);
                await mediator.Publish(new CreatedPointerFileNotification(request, pfwh), cancellationToken);

                // 2. Create the PointerFileEntry
                var pfe = PointerFileEntry.FromBinaryFileWithHash(bfwh);
                stateDbRepository.AddPointerFileEntry(pfe);

                await binaryFilesToDelete.Writer.WriteAsync(bfwh, cancellationToken);
            }

            binaryFilesToDelete.Writer.Complete();

        }, cancellationToken);


        // 7. Remove PointerFileEntries that do not exist on disk
        var removeDeletedPointerFileEntriesTask = Task.Run(async () =>
        {
            await pointerFileCreationTask; // C51

            RemoveDeletedPointerFileEntries(request, stateDbRepository);
        }, cancellationToken);


        // 8. Delete BinaryFiles
        var deleteBinaryFilesTask = Task.Run(async () =>
        {
            await foreach (var bfwh in binaryFilesToDelete.Reader.ReadAllAsync(cancellationToken))
            {
                if (request.RemoveLocal)
                {
                    bfwh.Delete();
                    logger.LogInformation("{flagName}: Deleted {bf}", nameof(request.RemoveLocal), bfwh);
                }
            }
        }, cancellationToken);

        // 9. Update Tier
        var updateTierTask = Task.Run(async () =>
        {
            //await Parallel.ForEachAsync(repository.GetChunks(), GetParallelOptions(request.UpdateTierBlock_Parallelism), async (b, ct) =>
            //{

            //});

            //await uploadBinariesTask;

            var chunksToUpdate = stateDbRepository.GetBinaryProperties().Where(bp => bp.StorageTier != request.Tier);

            await Parallel.ForEachAsync(chunksToUpdate, GetParallelOptions(request.UpdateTierBlock_Parallelism), async (bp, ct) =>
            {
                if (bp.StorageTier == StorageTier.Archive)
                    return;  // do not do mass hydration of archive tiers

                var effectiveTier = GetEffectiveStorageTier(request.StorageTiering, request.Tier, bp.ArchivedLength);

                if (bp.StorageTier == effectiveTier)
                    return;

                await repository.SetChunkStorageTierAsync(bp.Hash, effectiveTier, ct);
                stateDbRepository.UpdateBinaryStorageTier(bp.Hash, effectiveTier);

                logger.LogInformation("Updated Chunk {chunk} of size {size} from {originalTier} to {newTier}", bp.Hash, bp.ArchivedLength, bp.StorageTier, effectiveTier);
            });
        }, cancellationToken);


        await Task.WhenAll(indexTask, hashTask, uploadRouterTask, uploadBinariesTask, latentPointerTask, pointerFileCreationTask, removeDeletedPointerFileEntriesTask, deleteBinaryFilesTask);

        await mediator.Publish(new ArchiveCommandDoneNotification(request), cancellationToken);

        return;


        static Channel<T> GetBoundedChannel<T>(int capacity, bool singleWriter)
        {
            return Channel.CreateBounded<T>(GetBoundedChannelOptions(capacity, singleWriter));
        }

        static BoundedChannelOptions GetBoundedChannelOptions(int capacity, bool singleWriter)
        {
            return new BoundedChannelOptions(capacity)
            {
                FullMode                      = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
                SingleWriter                  = singleWriter,
                SingleReader                  = false
            };
        }

        ParallelOptions GetParallelOptions(int maxDegreeOfParallelism) => new() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };

        UploadStatus DetermineUploadStatus(Hash h)
        {
            lock (uploadingBinaries)
            {
                if (stateDbRepository.BinaryExists(h))
                    // Binary exists remotely
                    return UploadStatus.Uploaded;
                else
                {
                    var notUploading = uploadingBinaries.TryAdd(h, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)); // ALWAYS create a new TaskCompletionSource with the RunContinuationsAsynchronously option, otherwise the continuations will run on THIS thread -- https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md#always-create-taskcompletionsourcet-with-taskcreationoptionsruncontinuationsasynchronously
                    if (notUploading)
                        // Binary does not exist remotely and is not being uploaded
                        return UploadStatus.NotStarted;
                    else
                    // Binary does not exist remotely but is already being uploaded
                        return UploadStatus.Uploading;
                }
            }
        }

        void HasBeenUploaded(BinaryProperties bp)
        {
            lock (uploadingBinaries)
            {
                stateDbRepository.AddBinary(bp);
                uploadingBinaries[bp.Hash].SetResult();
                var r = uploadingBinaries.Remove(bp.Hash, out var value);

                if (!r)
                    throw new InvalidOperationException($"Tried to remove {bp.Hash} but it was not present");
            }
        }
    }




    public static StorageTier GetEffectiveStorageTier(Dictionary<long, StorageTier> tiering, StorageTier preferredTier, long size)
    {
        // Use the dictionary to determine if the size falls under a defined tier
        foreach (var entry in tiering)
            if (size <= entry.Key)
                return entry.Value;

        return preferredTier;
    }

    private enum UploadStatus
    {
        NotStarted,
        Uploading,
        Uploaded
    }


    internal static async Task<FilePairWithHash> HashFilesAsync(bool fastHash, IHashValueProvider hvp, FilePair pair)
    {
        if (pair.IsBinaryFileWithPointerFile)
        {
            // A PointerFile with corresponding BinaryFile
            if (fastHash)
            {
                // FastHash option - take the hash from the PointerFile
                if (pair.PointerFile!.LastWriteTimeUtc == pair.BinaryFile!.LastWriteTimeUtc)
                {
                    // The file has not been modified
                    var pfwh0 = PointerFileWithHash.FromExistingPointerFile(pair.PointerFile);
                    var bfwh0 = BinaryFileWithHash.FromBinaryFile(pair.BinaryFile, pfwh0.Hash);

                    return new(pfwh0, bfwh0);
                }
            }

            // Fasthash is off, or the File has been modified
            var h1    = await hvp.GetHashAsync(pair.BinaryFile!);
            var bfwh1 = BinaryFileWithHash.FromBinaryFile(pair.BinaryFile!, h1);

            var pfwh1 = PointerFileWithHash.FromExistingPointerFile(pair.PointerFile!);
            if (pfwh1.Hash != bfwh1.Hash)
                throw new InvalidOperationException($"The PointerFile {pfwh1} is not valid for the BinaryFile '{bfwh1.FullName}' (BinaryHash does not match). Has the BinaryFile been updated? Delete the PointerFile and try again.");

            return new(pfwh1, bfwh1);
        }
        else if (pair.IsPointerFileOnly)
        {
            // A PointerFile without a BinaryFile
            var pfwh = PointerFileWithHash.FromExistingPointerFile(pair.PointerFile!);

            return new(pfwh, null);
        }
        else if (pair.IsBinaryFileOnly)
        {
            // A BinaryFile without a PointerFile
            var h = await hvp.GetHashAsync(pair.BinaryFile!);
            var bfwh = BinaryFileWithHash.FromBinaryFile(pair.BinaryFile!, h);

            return new(null, bfwh);
        }
        else
            throw new InvalidOperationException("Both PointerFile and BinaryFile are null");
    }

    private static void RemoveDeletedPointerFileEntries(ArchiveCommand request, IStateDbRepository stateDbRepository)
    {
        foreach (var pfe in stateDbRepository.GetPointerFileEntries())
        {
            var f = File.FromRelativeName(request.LocalRoot, pfe.RelativeName);

            if (!f.Exists)
            {
                // The PointerFile does not exist on disk
                stateDbRepository.DeletePointerFileEntry(pfe);
            }
        }
    }
}