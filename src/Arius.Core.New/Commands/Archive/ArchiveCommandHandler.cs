using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Services;
using FluentValidation;
using MediatR;
using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Arius.Core.New.Commands.Archive;

public abstract record ArchiveCommandNotification : INotification
{
    protected ArchiveCommandNotification(ArchiveCommand command)
    {
        Command = command;
    }

    public ArchiveCommand Command { get; }
}

public record FilePairFoundNotification : ArchiveCommandNotification
{
    public FilePairFoundNotification(ArchiveCommand command) : base(command)
    {
    }

    public required FilePair FilePair { get; init; }
}

public record FilePairHashingNotification : ArchiveCommandNotification
{
    public FilePairHashingNotification(ArchiveCommand command) : base(command)
    {
    }

    public required FilePair FilePair { get; init; }
}
public record FilePairHashedNotification : ArchiveCommandNotification
{
    public FilePairHashedNotification(ArchiveCommand command) : base(command)
    {
    }

    public required FilePairWithHash FilePairWithHash { get; init; }
}

public record ArchiveCommandDoneNotification : ArchiveCommandNotification
{
    public ArchiveCommandDoneNotification(ArchiveCommand command) : base(command)
    {
    }
}

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
            foreach (var fp in IndexFiles(fileSystem, request.LocalRoot))
            {
                await Task.Delay(2000);
                
                await mediator.Publish(new FilePairFoundNotification(request) { FilePair = fp }, cancellationToken);
                logger.LogInformation("Found {fp}", fp);
                await filesToHash.Writer.WriteAsync(fp, cancellationToken);
            }

            filesToHash.Writer.Complete();
        }, cancellationToken);


        // 2. Hash the filepairs
        var hashedFilePairs = GetBoundedChannel<FilePairWithHash>(request.BinariesToUpload_BufferSize, false);
        var hvp              = new SHA256Hasher(request.Repository.Passphrase);
        var hashTask = Parallel.ForEachAsync(
            filesToHash.Reader.ReadAllAsync(cancellationToken),
            GetParallelOptions(request.Hash_Parallelism),
            async (filePair, ct) =>
            {
                await mediator.Publish(new FilePairHashingNotification(request) { FilePair = filePair }, ct);
                await Task.Delay(2000);
                var filePairWithHash = await HashFilesAsync(request.FastHash, hvp, filePair);
                await mediator.Publish(new FilePairHashedNotification(request) { FilePairWithHash = filePairWithHash }, ct);
                
                await hashedFilePairs.Writer.WriteAsync(filePairWithHash, ct);
            });

        hashTask.ContinueWith(_ => hashedFilePairs.Writer.Complete());

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
                        latentPointers.Add(pwh.PointerFile!);
                    }

                    switch (r)
                    {
                        case UploadStatus.NotStarted:
                            // 2.1 Does not yet exist remote and not yet being uploaded --> upload
                            logger.LogInformation("Binary for {relativeName} does not exist remotely. Starting upload.", pwh.RelativeName);

                            await binariesToUpload.Writer.WriteAsync(pwh.BinaryFile!, cancellationToken);

                            //stats.AddRemoteRepositoryStatistic(deltaBinaries: 1, deltaSize: ce.IncrementalLength);
                            break;
                        case UploadStatus.Uploading:
                            // 2.2 Does not yet exist remote but is already being uploaded
                            logger.LogInformation("Binary for {relativeName} does not exist remotely but is already being uploaded.", pwh.RelativeName);

                            addUploadedBinariesToPointerFileQueueTasks.Add(uploadingBinaries[pwh.Hash].Task.ContinueWith(async _ =>
                            {
                                logger.LogInformation("Binary for {relativeName} has been uploaded.", pwh.RelativeName);

                                await pointerFileEntriesToCreate.Writer.WriteAsync(pwh.BinaryFile!, cancellationToken);
                            }, cancellationToken));

                            break;
                        case UploadStatus.Uploaded:
                            // 2.3 Is already uploaded
                            logger.LogInformation("Binary for {relativeName} already exists remotely.", pwh.RelativeName);

                            await pointerFileEntriesToCreate.Writer.WriteAsync(pwh.BinaryFile!, cancellationToken);

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }, cancellationToken);


        // 4. Upload the binaries
        var repository = storageAccountFactory.GetRepository(request.Repository);
        var uploadBinaryTask = Parallel.ForEachAsync(
            binariesToUpload.Reader.ReadAllAsync(cancellationToken),
            GetParallelOptions(request.UploadBinaryFileBlock_BinaryFileParallelism),
            async (bfwh, ct) =>
            {
                    var bp = await repository.UploadChunkAsync(bfwh, ct);

                    HasBeenUploaded(bp);

                    await pointerFileEntriesToCreate.Writer.WriteAsync(bfwh, ct);
            }
        );

        // 5. Create PointerFileEntries and PointerFiles

        var pointerFileCreationTask = Task.Run(async () =>
        {
            await foreach (var pwh in pointerFileEntriesToCreate.Reader.ReadAllAsync(cancellationToken))
            {
            }

        }, cancellationToken);


        //// at this point we know that the binary has been uploaded -- we can create the pointerfileentry
        //await pointerFileEntriesToCreate.Writer.WriteAsync()





        //        // if not present on the remote
        //        if (stateDbRepository.BinaryExists(pwh.BinaryFile!.Hash))
        //        {
        //            // TODO binariesThatWillBeUploaded -- 
        //            //await stateDbRepository.UploadBinaryFileAsync(bfwh);
        //        }

        //        await pointerFileEntriesToCreate.Writer.WriteAsync(pwh.PointerFile, ct);
        //    });

        // upload Task --> Set completion source !!
        //var ce = await UploadChunkAsync(bf);
        //uploadingBinaries[filePairWithHash.Hash].SetResult();



        Task.WhenAll(hashTask/*, someTask*/).ContinueWith(_ => pointerFileEntriesToCreate.Writer.Complete());

        await Task.WhenAll(indexTask, hashTask, uploadRouterTask, addUploadedBinariesToPointerFileQueueTasks.WhenAll());

        //Iterate over all 'stale' pointers (pointers that were present but did not have a remote binary
        foreach (var pf in latentPointers)
        {
            if (!stateDbRepository.BinaryExists(pf.Hash))
                throw new InvalidOperationException($"PointerFile {pf.RelativeName} exists on disk but no corresponding binary exists either locally or remotely.");
        }

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

        ParallelOptions GetParallelOptions(int maxDegreeOfParallelism)
        {
            return new ParallelOptions() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };
        }

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
                var r = uploadingBinaries.Remove(bp.Hash, out var value);

                if (!r)
                    throw new InvalidOperationException($"Tried to remove {bp.Hash} but it was not present");
            }
        }
    }

    private enum UploadStatus
    {
        NotStarted,
        Uploading,
        Uploaded
    }


    internal static IEnumerable<FilePair> IndexFiles(IFileSystem fileSystem, DirectoryInfo root)
    {
        foreach (var file in fileSystem.EnumerateFiles(root))
        {
            if (file.IsPointerFile)
            {
                // this is a PointerFile
                var pf = file.GetPointerFile(root);

                if (pf.GetBinaryFile(root) is { Exists: true } bf)
                {
                    // 1. BinaryFile exists too
                    yield return new(pf, bf);
                }
                else
                {
                    // 2. BinaryFile does not exist
                    yield return new(pf, null);
                }
            }
            else
            {
                // this is a BinaryFile
                var bf = file.GetBinaryFile(root);

                if (bf.GetPointerFile(root) is { Exists: true } pf)
                {
                    // 3. PointerFile exists too -- DO NOT YIELD ANYTHING; this pair has been yielded in (1)
                    continue;
                }
                else
                {
                    // 4. BinaryFile does not exist
                    yield return new(null, bf);
                }
            }
        }

        //var seenFiles        = new HashSet<string>();
        //var currentDirectory = root.FullName;

        //foreach (var file in fileSystem.EnumerateFiles(root))
        //{
        //    // Check if the directory has changed, if so clear the seenFiles HashSet
        //    if (!string.Equals(currentDirectory, file.Path, StringComparison.OrdinalIgnoreCase))
        //    {
        //        seenFiles.Clear();
        //        currentDirectory = file.Path; // Update the current directory to the file's directory
        //    }

        //    if (!seenFiles.Add(file.BinaryFileFullName))
        //        continue;

        //    if (file.IsPointerFile)
        //    {
        //        // this is a PointerFile
        //        var pf = file.GetPointerFile(root);

        //        if (pf.GetBinaryFile(root) is { Exists: true } bf)
        //        {
        //            // BinaryFile exists too
        //            yield return new(pf, bf);
        //        }
        //        else
        //        {
        //            // BinaryFile does not exist
        //            yield return new(pf, null);
        //        }
        //    }
        //    else
        //    {
        //        // this is a BinaryFile
        //        var bf = file.GetBinaryFile(root);

        //        if (bf.GetPointerFile(root) is { Exists : true } pf)
        //        {
        //            // PointerFile exists too
        //            yield return new(pf, bf);
        //        }
        //        else
        //        {
        //            // BinaryFile does not exist
        //            yield return new(null, bf);
        //        }
        //    }
        //}
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
                    var pfwh0 = pair.PointerFile.GetPointerFileWithHash();
                    var bfwh0 = pair.BinaryFile.GetBinaryFileWithHash(pfwh0.Hash);

                    return new(pfwh0, bfwh0);
                }
            }

            // Fasthash is off, or the File has been modified
            var h1    = await hvp.GetHashAsync(pair.BinaryFile!);
            var bfwh1 = pair.BinaryFile!.GetBinaryFileWithHash(h1);

            var pfwh1 = pair.PointerFile!.GetPointerFileWithHash();
            if (pfwh1.Hash != bfwh1.Hash)
                throw new InvalidOperationException($"The PointerFile {pfwh1} is not valid for the BinaryFile '{bfwh1.FullName}' (BinaryHash does not match). Has the BinaryFile been updated? Delete the PointerFile and try again.");

            return new(pfwh1, bfwh1);
        }
        else if (pair.IsPointerFileOnly)
        {
            // A PointerFile without a BinaryFile
            var pfwh = pair.PointerFile!.GetPointerFileWithHash();

            return new(pfwh, null);
        }
        else if (pair.IsBinaryFileOnly)
        {
            // A BinaryFile without a PointerFile
            var h = await hvp.GetHashAsync(pair.BinaryFile!);
            var bfwh = pair.BinaryFile!.GetBinaryFileWithHash(h);

            return new(null, bfwh);
        }
        else
            throw new InvalidOperationException("Both PointerFile and BinaryFile are null");
    }

    ///// <summary>
    ///// Ensure the PointerFile is created
    ///// </summary>
    ///// <param name="pair"></param>
    ///// <returns>A pair where the PointerFile is not null</returns>
    ///// <exception cref="InvalidOperationException"></exception>
    //internal static FilePairWithHash CreatePointerIfNotExist(FilePairWithHash pair)
    //{
    //    if (pair.PointerFile is not null && pair.BinaryFile is not null)
    //    {
    //        // A PointerFile with corresponding BinaryFile
    //        return pair;
    //    }
    //    else if (pair.PointerFile is not null && pair.BinaryFile is null)
    //    {
    //        // A PointerFile without a BinaryFile
    //        return pair;
    //    }
    //    else if (pair.PointerFile is null && pair.BinaryFile is not null)
    //    {
    //        // A BinaryFile without a PointerFile
    //        var pfwh = pair.BinaryFile.GetPointerFileWithHash();
    //        pfwh.Save();

    //        return new FilePairWithHash(pfwh, pair.BinaryFile);
    //    }
    //    else
    //        throw new InvalidOperationException("Both PointerFile and BinaryFile are null");

    //    //if (pair.PointerFile is not null && pair.BinaryFile is not null)
    //    //{
    //    //    // A PointerFile with corresponding BinaryFile
    //    //    return pair.PointerFile;
    //    //}
    //    //else if (pair.PointerFile is not null && pair.BinaryFile is null)
    //    //{
    //    //    // A PointerFile without a BinaryFile
    //    //    return pair.PointerFile;
    //    //}
    //    //else if (pair.PointerFile is null && pair.BinaryFile is not null)
    //    //{
    //    //    // A BinaryFile without a PointerFile
    //    //    var pfwh = pair.BinaryFile.GetPointerFileWithHash();
    //    //    pfwh.Save();

    //    //    return pfwh;
    //    //}
    //    //else
    //    //    throw new InvalidOperationException("Both PointerFile and BinaryFile are null");
    //}
}