using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Archive;

internal partial class ArchiveCommandHandler : AsyncCommand<ArchiveCommand>, IRequestHandler<ArchiveCommand, CommandResultStatus>
{
    public ArchiveCommandHandler(ILoggerFactory loggerFactory, Repository repo, ArchiveCommandStatistics statisticsProvider)
    {
        this.loggerFactory = loggerFactory;
        this.repo          = repo;
        this.logger        = loggerFactory.CreateLogger<ArchiveCommandHandler>();
        this.stats         = statisticsProvider;

        this.fileSystemService = new FileSystemService(loggerFactory.CreateLogger<FileSystemService>());
    }

    private readonly ILoggerFactory                 loggerFactory;
    private readonly Repository                     repo;
    private readonly ILogger<ArchiveCommandHandler> logger;
    private readonly ArchiveCommandStatistics       stats;
    private readonly FileSystemService              fileSystemService;

    public async Task<CommandResultStatus> Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(request);
    }

    protected override async Task<CommandResultStatus> ExecuteImplAsync(ArchiveCommand options)
    {
        var hashValueProvider = new SHA256Hasher(options);
        var fileService       = new FileService(loggerFactory.CreateLogger<FileService>(), hashValueProvider);

        var binariesToUpload           = Channel.CreateBounded<BinaryFile>(new BoundedChannelOptions(options.BinariesToUpload_BufferSize) { FullMode            = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var pointerFileEntriesToCreate = Channel.CreateBounded<PointerFile>(new BoundedChannelOptions(options.PointerFileEntriesToCreate_BufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var binariesToDelete           = Channel.CreateBounded<BinaryFile>(new BoundedChannelOptions(options.BinariesToDelete_BufferSize) { FullMode            = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var binaryFileUploadCompleted  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);


        // Get statistics of before the run
        var startStats = await repo.GetStatisticsAsync();
        stats.AddRemoteRepositoryStatistic(
            beforeBinaries: startStats.BinaryCount,
            beforeSize: startStats.ChunkSize,
            beforePointerFileEntries: startStats.CurrentPointerFileEntryCount);


        var indexBlock = new IndexBlock(this,
            sourceFunc: () => options.Path,
            onCompleted: () => { },
            maxDegreeOfParallelism: options.IndexBlock_Parallelism,
            options: options,
            fileService: fileService,
            onIndexedPointerFile: async pf =>
            {
                await pointerFileEntriesToCreate.Writer.WriteAsync(pf); //B301
            },
            onIndexedBinaryFile: async arg =>
            {
                var (bf, alreadyBackedUp) = arg;
                if (alreadyBackedUp)
                {
                    if (options.RemoveLocal)
                        await binariesToDelete.Writer.WriteAsync(bf); //B401 //NOTE B1202 deletes NEWLY archived binaries, this one deletes EXISTING binaries //TODO test the flow
                    //else - discard //B304
                }
                else
                    await binariesToUpload.Writer.WriteAsync(bf); //B302
            },
            onBinaryFileIndexCompleted: () =>
            {
                binariesToUpload.Writer.Complete(); //B310
            },
            binaryFileUploadCompletedTaskCompletionSource: binaryFileUploadCompleted);
        var indexTask = indexBlock.GetTask;


        var pointersToCreate = Channel.CreateBounded<BinaryFile>(new BoundedChannelOptions(options.PointersToCreate_BufferSize) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = false });
        var uploadBinaryFileBlock = new UploadBinaryFileBlock(this,
            sourceFunc: () => binariesToUpload,
            maxDegreeOfParallelism: options.UploadBinaryFileBlock_BinaryFileParallelism,
            onCompleted: () =>
            {
                pointersToCreate.Writer.Complete(); //B410
                binaryFileUploadCompleted.SetResult(); //B411
            }, 
            options: options, 
            hashValueProvider: hashValueProvider,
            onBinaryExists: async bf =>
            {
                await pointersToCreate.Writer.WriteAsync(bf); //B403
            });
        var uploadBinaryFileTask = uploadBinaryFileBlock.GetTask;


        var createPointerFileIfNotExistsBlock = new CreatePointerFileIfNotExistsBlock(this,
            sourceFunc: () => pointersToCreate, //B1201
            onCompleted: () => binariesToDelete.Writer.Complete(),
            maxDegreeOfParallelism: options.CreatePointerFileIfNotExistsBlock_Parallelism,
            fileService: fileService,
            
            onSuccesfullyBackedUp: async bf =>
            {
                if (options.RemoveLocal)
                    await binariesToDelete.Writer.WriteAsync(bf); //B1202
            }, 
            onPointerFileCreated: async pf => await pointerFileEntriesToCreate.Writer.WriteAsync(pf) /* B1310 */);
        var createPointerFileIfNotExistsTask = createPointerFileIfNotExistsBlock.GetTask;


#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        // can be ignored since we'll be awaiting the pointersToCreate
        Task.WhenAll(indexTask, createPointerFileIfNotExistsTask)
            .ContinueWith(_ => pointerFileEntriesToCreate.Writer.Complete()); //B1210 - these are the two only blocks that write to this blockingcollection. If these are both done, adding is completed.
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        
        var createPointerFileEntryIfNotExistsBlock = new CreatePointerFileEntryIfNotExistsBlock(this,
            sourceFunc: () => pointerFileEntriesToCreate,
            onCompleted: () => { }, 
            maxDegreeOfParallelism: options.CreatePointerFileEntryIfNotExistsBlock_Parallelism, 
            options: options);
        var createPointerFileEntryIfNotExistsTask = createPointerFileEntryIfNotExistsBlock.GetTask;


        var deleteBinaryFilesBlock = new DeleteBinaryFilesBlock(this,
            sourceFunc: () => binariesToDelete,
            onCompleted: () => { }, 
            maxDegreeOfParallelism: options.DeleteBinaryFilesBlock_Parallelism);
        var deleteBinaryFilesTask = deleteBinaryFilesBlock.GetTask;


        var createDeletedPointerFileEntryForDeletedPointerFilesBlock = new CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(this,
            sourceFunc: async () =>
            {
                var pointerFileEntriesToCheckForDeletedPointers = Channel.CreateUnbounded<PointerFileEntry>(new UnboundedChannelOptions { AllowSynchronousContinuations = false, SingleWriter = true, SingleReader = false });
                var pfes = repo.GetCurrentPointerFileEntriesAsync(includeDeleted: false)
                    .Where(pfe => pfe.VersionUtc < options.VersionUtc) // that were not created in the current run (those are assumed to be up to date)
                    .ToEnumerable(); 
                await pointerFileEntriesToCheckForDeletedPointers.Writer.AddFromEnumerable(pfes, completeAddingWhenDone: true); //B1401
                return pointerFileEntriesToCheckForDeletedPointers;
            },
            maxDegreeOfParallelism: options.CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism,
            onCompleted: () => { },
            options: options,
            fileService: fileService);
        var createDeletedPointerFileEntryForDeletedPointerFilesTask = createDeletedPointerFileEntryForDeletedPointerFilesBlock.GetTask;


        var updateTierBlock = new UpdateTierBlock(this,
            sourceFunc: () => repo,
            onCompleted: () => { }, maxDegreeOfParallelism: options.UpdateTierBlock_Parallelism, options: options);
        var updateTierTask = updateTierBlock.GetTask;


        // Await the current stage of the pipeline
        await Task.WhenAny(Task.WhenAll(BlockBase.AllTasks), BlockBase.CancellationTask);


        // Get statistics after the run
        var endStats = await repo.GetStatisticsAsync();
        stats.AddRemoteRepositoryStatistic(
            afterBinaries: endStats.BinaryCount,
            afterSize: endStats.ChunkSize,
            afterPointerFileEntries: endStats.CurrentPointerFileEntryCount);
        var vs = await repo.GetVersionsAsync().ToArrayAsync();
        stats.versionCount = vs.Length;
        stats.lastVersion = vs.Last();

        // save the state in any case even in case of errors otherwise the info on BinaryProperties is lost
        await repo.SaveStateToRepositoryAsync(options.VersionUtc);

        if (BlockBase.AllTasks.Where(t => t.Status == TaskStatus.Faulted) is var ts && ts.Any())
        {
            var exceptions = ts.Select(t => t.Exception);
            foreach (var e in exceptions)
                logger.LogError(e);

            throw new AggregateException(exceptions);
        }

        return CommandResultStatus.Success;
    }
}