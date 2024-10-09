using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Services;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using FluentValidation;
using Humanizer;
using Humanizer.Bytes;
using MediatR;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using File = Arius.Core.Infrastructure.Storage.LocalFileSystem.File;
using TaskExtensions = WouterVanRanst.Utils.Extensions.TaskExtensions;

namespace Arius.Core.New.Commands.Archive;

public abstract record ArchiveCommandNotification(ArchiveCommand Command) : INotification;
public record FilePairFoundNotification(ArchiveCommand Command, IFilePair FilePair) : ArchiveCommandNotification(Command);
public record FilePairHashingStartedNotification(ArchiveCommand Command, IFilePair FilePair) : ArchiveCommandNotification(Command);
public record FilePairHashingCompletedNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record BinaryFileToUploadNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record BinaryFileWaitingForOtherUploadNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record BinaryFileWaitingForOtherUploadDoneNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record BinaryFileAlreadyUploadedNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record UploadBinaryFileStartedNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record UploadBinaryFileCompletedNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash, long OriginalLength, long ArchivedLength, double UploadSpeedMBps) : ArchiveCommandNotification(Command);
public record CreatedPointerFileNotification(ArchiveCommand Command, IPointerFileWithHash PointerFile) : ArchiveCommandNotification(Command);
public record UpdatedPointerFileNotification(ArchiveCommand Command, IPointerFileWithHash PointerFile) : ArchiveCommandNotification(Command);
public record DeletedPointerFileEntryNotification(ArchiveCommand Command, string RelativeName) : ArchiveCommandNotification(Command);
public record CreatedPointerFileEntryNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record UpdatedPointerFileEntryNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record DeletedBinaryFileNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);
public record UpdatedChunkTierNotification(ArchiveCommand Command, Hash Hash, long ArchivedLength, StorageTier OriginalTier, StorageTier NewTier) : ArchiveCommandNotification(Command);
public record NewStateVersionCreatedNotification(ArchiveCommand Command, StateVersion Version) : ArchiveCommandNotification(Command);
public record NoNewStateVersionCreatedNotification(ArchiveCommand Command) : ArchiveCommandNotification(Command);
public record ArchiveCommandDoneNotification(ArchiveCommand Command) : ArchiveCommandNotification(Command);


internal class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{
    private readonly IMediator                      mediator;
    private readonly AriusConfiguration             config;
    private readonly IFileSystem                    fileSystem;
    private readonly PointerFileSerializer          pointerFileSerializer;
    private readonly IStorageAccountFactory         storageAccountFactory;
    private readonly ILogger<ArchiveCommandHandler> logger;

    public ArchiveCommandHandler(
        IMediator mediator,
        IOptions<AriusConfiguration> config,
        IFileSystem fileSystem,
        PointerFileSerializer pointerFileSerializer,
        IStorageAccountFactory storageAccountFactory,
        ILogger<ArchiveCommandHandler> logger)
    {
        this.mediator              = mediator;
        this.config                = config.Value;
        this.fileSystem            = fileSystem;
        this.pointerFileSerializer = pointerFileSerializer;
        this.storageAccountFactory = storageAccountFactory;
        this.logger                = logger;
    }

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        await new ArchiveCommandValidator().ValidateAndThrowAsync(request, cancellationToken);

        // Create a linked token source to cancel all tasks when the main token is cancelled
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = cancellationTokenSource.Token;

        // Get the Repositories
        var remoteRepository = storageAccountFactory.GetRemoteRepository(request.RemoteRepositoryOptions);
        var remoteStateRepository = remoteRepository.GetRemoteStateRepository();

        // Start download of the latest state database
        var getLocalStateDbRepositoryTask = Task.Run(async () =>
        {
            var localStateDatabaseCacheDirectory = config.GetLocalStateDatabaseCacheDirectoryForContainerName(request.RemoteRepositoryOptions.ContainerName);
            return await remoteStateRepository.CreateNewLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, request.Version, basedOn: null);
        }, cancellationToken);


        // 1. Index the request.LocalRoot
        var filesToHash = GetBoundedChannel<IFilePair>(request.FilesToHash_BufferSize, true);
        var indexTask = Task.Run(async () =>
        {
            foreach (var fp in fileSystem.EnumerateFilePairs(request.LocalRoot))
            {
                logger.LogInformation("Found {fp}", fp);
                await mediator.Publish(new FilePairFoundNotification(request, fp), cancellationToken);
             
                await filesToHash.Writer.WriteAsync(fp, cancellationToken); // A10
            }

            filesToHash.Writer.Complete(); // C10
        }, cancellationToken);


        // 2. Hash the filepairs
        var hashedFilePairs = GetBoundedChannel<IFilePairWithHash>(request.BinariesToUpload_BufferSize, false);
        var hvp              = new SHA256Hasher(request.RemoteRepositoryOptions.Passphrase);
        var hashTask = Parallel.ForEachAsync(
            filesToHash.Reader.ReadAllAsync(cancellationToken),
            GetParallelOptions(request.Hash_Parallelism),
            async (fp, ct) =>
            {
                logger.LogInformation("Started hashing {fp}...", fp);
                await mediator.Publish(new FilePairHashingStartedNotification(request, fp), ct);

                var filePairWithHash = await HashFilesAsync(request.FastHash, pointerFileSerializer, hvp, fp);

                logger.LogInformation("Started hashing {fp}... done", fp);
                await mediator.Publish(new FilePairHashingCompletedNotification(request, filePairWithHash), ct);

                await hashedFilePairs.Writer.WriteAsync(filePairWithHash, ct); // 20
            });

        hashTask.ContinueWith(_ => hashedFilePairs.Writer.Complete(), cancellationToken); // C20

        var localStateDbRepository = await getLocalStateDbRepositoryTask;


        // 3. Decide whether binaries need uploading
        var binariesToUpload           = GetBoundedChannel<IFilePairWithHash>(request.BinariesToUpload_BufferSize,           false);
        var pointerFileEntriesToCreate = GetBoundedChannel<IFilePairWithHash>(request.PointerFileEntriesToCreate_BufferSize, false);
        var latentPointers             = new ConcurrentBag<IPointerFileWithHash>();

        var uploadingBinaries = new Dictionary<Hash, TaskCompletionSource>();
        var addUploadedBinariesToPointerFileQueueTasks = new ConcurrentBag<Task>();

        var uploadRouterTask = Task.Run(async () =>
        {
            await foreach (var pwh in hashedFilePairs.Reader.ReadAllAsync(cancellationToken))
            {
                if (pwh.HasExistingBinaryFile)
                {
                    // The binary exists locally
                    var r = DetermineUploadStatus(pwh.Hash);

                    if (pwh.HasExistingPointerFile && r is not UploadStatus.Uploaded)
                    {
                        // edge case: the PointerFile already exists but the binary is not uploaded (yet) -- eg when re-uploading an entire archive -> check them later
                        latentPointers.Add(pwh.PointerFile!); // A31
                        // TODO LOG
                        // TODO MEDIATR
                    }

                    switch (r)
                    {
                        case UploadStatus.NotStarted:
                            // 2.1 Does not yet exist remote and not yet being uploaded --> upload
                            logger.LogInformation("Binary for {relativeName} does not exist remotely. Starting upload.", pwh.RelativeName);

                            await binariesToUpload.Writer.WriteAsync(pwh, cancellationToken); // A32

                            //stats.AddRemoteRepositoryStatistic(deltaBinaries: 1, deltaSize: ce.IncrementalLength);
                            break;
                        case UploadStatus.Uploading:
                            // 2.2 Does not yet exist remote but is already being uploaded
                            logger.LogInformation("Binary for {relativeName} does not exist remotely but is already being uploaded.", pwh.RelativeName);
                            await mediator.Publish(new BinaryFileWaitingForOtherUploadNotification(request, pwh), cancellationToken);

                            addUploadedBinariesToPointerFileQueueTasks.Add(uploadingBinaries[pwh.Hash].Task.ContinueWith(async _ =>
                            {
                                logger.LogInformation("Binary for {relativeName} has been uploaded.", pwh.RelativeName);
                                await mediator.Publish(new BinaryFileWaitingForOtherUploadDoneNotification(request, pwh), cancellationToken);

                                await pointerFileEntriesToCreate.Writer.WriteAsync(pwh, cancellationToken); // A332
                            }, cancellationToken)); // A331

                            break;
                        case UploadStatus.Uploaded:
                            // 2.3 Is already uploaded
                            logger.LogInformation("Binary for {relativeName} already exists remotely.", pwh.RelativeName);
                            await mediator.Publish(new BinaryFileAlreadyUploadedNotification(request, pwh), cancellationToken);

                            await pointerFileEntriesToCreate.Writer.WriteAsync(pwh, cancellationToken); // A35

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            binariesToUpload.Writer.Complete(); // C30
        }, cancellationToken);
        

        // 4. Upload the binaries
        var uploadBinariesTask = Parallel.ForEachAsync(
            binariesToUpload.Reader.ReadAllAsync(cancellationToken),
            GetParallelOptions(request.UploadBinaryFileBlock_BinaryFileParallelism),
            async (fpwh, ct) =>
            {
                logger.LogInformation("Uploading '{hash}' ({binaryFile}) ({size})...", fpwh.Hash.ToShortString(), fpwh.RelativeName, ByteSize.FromBytes(fpwh.BinaryFile.Length).Humanize());
                await mediator.Publish(new UploadBinaryFileStartedNotification(request, fpwh), ct);

                var stopwatch = Stopwatch.StartNew();
                var bp = await remoteRepository.UploadBinaryFileAsync(fpwh.BinaryFile, s => GetEffectiveStorageTier(request.StorageTiering, request.Tier, s), ct);
                stopwatch.Stop();
                
                var elapsedTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                var uploadSpeedMbps = ByteSize.FromBytes(bp.ArchivedSize).Megabytes / elapsedTimeInSeconds;

                logger.LogInformation("Uploading '{hash}' ({binaryFile}) ({size})... done in {elapsedTimeInSeconds} @ {speed:F2} MBps", fpwh.Hash.ToShortString(), fpwh.RelativeName, ByteSize.FromBytes(fpwh.BinaryFile.Length).Humanize(), stopwatch.Elapsed.Humanize(precision: 2), uploadSpeedMbps);
                await mediator.Publish(new UploadBinaryFileCompletedNotification(request, fpwh, bp.OriginalSize, bp.ArchivedSize, uploadSpeedMbps), ct);

                HasBeenUploaded(bp); // A41

                await pointerFileEntriesToCreate.Writer.WriteAsync(fpwh, ct); // A40
            }
        );

        Task.WhenAll(uploadBinariesTask, uploadRouterTask)
            .ContinueWith(async _ =>
            {
                await addUploadedBinariesToPointerFileQueueTasks.WhenAll();
                pointerFileEntriesToCreate.Writer.Complete();
            }, cancellationToken); // C40


        //5. Now that all binaries are uploaded, check the 'stale' pointers (pointers that were present but did not have a remote binary)
        var latentPointerTask = Task.Run(async () =>
        {
            await uploadBinariesTask; // C41 

            foreach (var pf in latentPointers)
            {
                if (!localStateDbRepository.BinaryExists(pf.Hash))
                    throw new InvalidOperationException($"PointerFile {pf.RelativeName} exists on disk but no corresponding binary exists either locally or remotely.");
            }
        }, cancellationToken);


        // 6. Create PointerFileEntries and PointerFiles
        var binaryFilesToDelete = GetBoundedChannel<IFilePairWithHash>(request.BinariesToDelete_BufferSize, true);
        var pointerFileCreationTask = Task.Run(async () =>
        {
            await foreach (var fpwh in pointerFileEntriesToCreate.Reader.ReadAllAsync(cancellationToken))
            {
                // 1. Create the PointerFile
                var (creationResult, pfwh) = pointerFileSerializer.CreateIfNotExists(fpwh.BinaryFile);

                switch (creationResult)
                {
                    case PointerFileSerializer.CreationResult.Created:
                        logger.LogInformation("Created PointerFile {pointerFile}", pfwh.RelativeName);
                        await mediator.Publish(new CreatedPointerFileNotification(request, pfwh), cancellationToken);
                        break;
                    case PointerFileSerializer.CreationResult.Overwritten:
                        logger.LogInformation("Updated PointerFile {pointerFile}", pfwh.RelativeName);
                        await mediator.Publish(new UpdatedPointerFileNotification(request, pfwh), cancellationToken);
                        break;
                    case PointerFileSerializer.CreationResult.Existed:
                        logger.LogDebug("Unchanged PointerFile {pointerFile}", pfwh.RelativeName);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                // 2. Create the PointerFileEntry
                var pfe = PointerFileEntry.FromBinaryFileWithHash(fpwh.BinaryFile);
                var upsertResult = localStateDbRepository.UpsertPointerFileEntry(pfe);

                switch (upsertResult)
                {
                    case UpsertResult.Added:
                        logger.LogInformation("Added PointerFileEntry for {binaryFile}", fpwh.RelativeName);
                        await mediator.Publish(new CreatedPointerFileEntryNotification(request, fpwh), cancellationToken);
                        break;
                    case UpsertResult.Updated:
                        logger.LogInformation("Updated PointerFileEntry for {binaryFile}", fpwh.RelativeName);
                        await mediator.Publish(new UpdatedPointerFileEntryNotification(request, fpwh), cancellationToken);
                        break;
                    case UpsertResult.Unchanged:
                        logger.LogDebug("Unchanged PointerFileEntry for {binaryFile}", fpwh.RelativeName);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                //stats.AddLocalStateRepositoryStatistic(deltaPointerFilesEntry: 1);

                await binaryFilesToDelete.Writer.WriteAsync(fpwh, cancellationToken);
            }
             
            binaryFilesToDelete.Writer.Complete(); // C52
        }, cancellationToken);


        // 7. Remove PointerFileEntries that do not exist on disk
        var removeDeletedPointerFileEntriesTask = Task.Run(async () =>
        {
            await pointerFileCreationTask; // C51

            await RemoveDeletedPointerFileEntriesAsync(request, localStateDbRepository, logger, mediator);
        }, cancellationToken);


        // 8. Delete BinaryFiles
        var deleteBinaryFilesTask = Task.Run(async () =>
        {

            await foreach (var fpwh in binaryFilesToDelete.Reader.ReadAllAsync(cancellationToken))
            {
                if (!request.RemoveLocal) 
                    continue;

                fpwh.BinaryFile.Delete();

                logger.LogInformation("{flagName}: Deleted {bf}", nameof(request.RemoveLocal), fpwh.BinaryFile);
                await mediator.Publish(new DeletedBinaryFileNotification(request, fpwh), cancellationToken);
            }
        }, cancellationToken);


        // 9. Update Tier
        var updateTierTask = Task.Run(async () =>
        {
            //await Parallel.ForEachAsync(repository.GetChunks(), GetParallelOptions(request.UpdateTierBlock_Parallelism), async (b, ct) => {});

            var chunksToUpdate = localStateDbRepository.GetBinaryProperties().Where(bp => bp.StorageTier != request.Tier);

            await Parallel.ForEachAsync(chunksToUpdate, GetParallelOptions(request.UpdateTierBlock_Parallelism), async (bp, ct) =>
            {
                if (bp.StorageTier == StorageTier.Archive)
                    return;  // do not do mass hydration of archive tiers

                var effectiveTier = GetEffectiveStorageTier(request.StorageTiering, request.Tier, bp.ArchivedSize);

                if (bp.StorageTier == effectiveTier)
                    return;

                await remoteRepository.SetBinaryStorageTierAsync(bp.Hash, effectiveTier, ct);
                localStateDbRepository.UpdateBinaryStorageTier(bp.Hash, effectiveTier);

                logger.LogInformation("Updated Chunk {chunk} of size {size} from {originalTier} to {newTier}", bp.Hash, bp.ArchivedSize, bp.StorageTier, effectiveTier);
                await mediator.Publish(new UpdatedChunkTierNotification(request, bp.Hash, bp.ArchivedSize, bp.StorageTier, effectiveTier), ct);
            });
        }, cancellationToken);

        await TaskExtensions.WhenAllWithCancellationAsync([indexTask, hashTask, uploadRouterTask, uploadBinariesTask, latentPointerTask, pointerFileCreationTask, removeDeletedPointerFileEntriesTask, deleteBinaryFilesTask, updateTierTask], cancellationTokenSource);

        var changes = await localStateDbRepository.UploadAsync();
        if (changes)
            // NOTE: This is logged in the SaveChangesAsync method
            await mediator.Publish(new NewStateVersionCreatedNotification(request, localStateDbRepository.Version), cancellationToken);
        else
            // NOTE: This is logged in the SaveChangesAsync method
            await mediator.Publish(new NoNewStateVersionCreatedNotification(request), cancellationToken);

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
                if (localStateDbRepository.BinaryExists(h))
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
                localStateDbRepository.AddBinary(bp);
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


    internal static async Task<IFilePairWithHash> HashFilesAsync(bool fastHash, PointerFileSerializer pointerFileSerializer, IHashValueProvider hvp, IFilePair pair)
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
                    var pfwh0 = pointerFileSerializer.FromExistingPointerFile(pair.PointerFile);
                    var bfwh0 = BinaryFileWithHash.FromBinaryFile(pair.BinaryFile, pfwh0.Hash);

                    return FilePairWithHash.FromFilePair(pfwh0, bfwh0);
                }
            }

            // Fasthash is off, or the File has been modified
            var h1    = await hvp.GetHashAsync(pair.BinaryFile!);
            var bfwh1 = BinaryFileWithHash.FromBinaryFile(pair.BinaryFile!, h1);

            var pfwh1 = pointerFileSerializer.FromExistingPointerFile(pair.PointerFile!);
            if (pfwh1.Hash != bfwh1.Hash)
                throw new InvalidOperationException($"The PointerFile {pfwh1} is not valid for the BinaryFile '{bfwh1.FullName}' (BinaryHash does not match). Has the BinaryFile been updated? Delete the PointerFile and try again.");

            return FilePairWithHash.FromFilePair(pfwh1, bfwh1);
        }
        else if (pair.IsPointerFileOnly)
        {
            // A PointerFile without a BinaryFile
            var pfwh = pointerFileSerializer.FromExistingPointerFile(pair.PointerFile!);

            return FilePairWithHash.FromPointerFile(pfwh);
        }
        else if (pair.IsBinaryFileOnly)
        {
            // A BinaryFile without a PointerFile
            var h = await hvp.GetHashAsync(pair.BinaryFile!);
            var bfwh = BinaryFileWithHash.FromBinaryFile(pair.BinaryFile!, h);

            return FilePairWithHash.FromBinaryFile(bfwh);
        }
        else
            throw new InvalidOperationException("Both PointerFile and BinaryFile are null");
    }

    private static async Task RemoveDeletedPointerFileEntriesAsync(ArchiveCommand request, ILocalStateRepository localStateRepository, ILogger logger, IMediator mediator)
    {
        foreach (var pfe in localStateRepository.GetPointerFileEntries())
        {
            var f = File.FromRelativeName(request.LocalRoot, pfe.RelativeName);

            if (!f.Exists)
            {
                // The PointerFile does not exist on disk
                localStateRepository.DeletePointerFileEntry(pfe);

                logger.LogInformation("Deleted PointerFileEntry for {relativeName}", pfe.RelativeName);
                await mediator.Publish(new DeletedPointerFileEntryNotification(request, pfe.RelativeName));
            }
        }
    }
}