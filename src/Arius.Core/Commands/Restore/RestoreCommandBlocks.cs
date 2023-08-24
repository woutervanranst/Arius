using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Restore;

internal class ProvisionPointerFilesBlock : TaskBlockBase<DirectoryInfo>
{
    public ProvisionPointerFilesBlock(ILoggerFactory loggerFactory,
       Func<DirectoryInfo> sourceFunc,
       int maxDegreeOfParallelism,
       IRestoreCommandOptions options,
       Repository repo,
       FileSystemService fileSystemService,
       FileService fileService,
       Func<PointerFile, Task> onIndexedPointerFile,
       Action onCompleted)
       : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, onCompleted: onCompleted)
    {
        this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        this.options                = options;
        this.repo                   = repo;
        this.fileSystemService      = fileSystemService;
        this.fileService            = fileService;
        this.onIndexedPointerFile   = onIndexedPointerFile;
    }

    private readonly int                     maxDegreeOfParallelism;
    private readonly IRestoreCommandOptions  options;
    private readonly Repository              repo;
    private readonly FileSystemService       fileSystemService;
    private readonly FileService             fileService;
    private readonly Func<PointerFile, Task> onIndexedPointerFile;

    protected override async Task TaskBodyImplAsync(DirectoryInfo root)
    {
        if (options.Synchronize)
        {
            Synchronize((dynamic)options);
        }
        
        await Index(root);

        

        /*
        //async Task SynchronizeAllPointerFiles(DirectoryInfo root)
        //{
        //    var existingPointerFileEntries = new HashSet<string>();

        //    // Get the PointerFiles for the given PointerFileEntries. Create PointerFiles if they do not exist.
        //    await foreach (var pfe in repo.GetPointerFileEntriesAsync(options.PointInTimeUtc, includeDeleted: false))
        //    {
        //        var (_, pf) = fileService.CreatePointerFileIfNotExists(root, pfe);

        //        logger.LogInformation($"PointerFile '{pf}' created");

        //        existingPointerFileEntries.Add(pfe.RelativeName);
        //    }

        //    // Delete the PointerFiles that do not exist in the given PointerFileEntries.
        //    foreach (var pfi in fileSystemService.GetPointerFileInfos(root))
        //    {
        //        var rn = pfi.GetRelativeName(root);
        //        if (!existingPointerFileEntries.Contains(rn))
        //        {
        //            pfi.Delete();
        //            logger.LogInformation($"PointerFile '{rn}' deleted");
        //        }
        //    }

        //    root.DeleteEmptySubdirectories();
        //}

        //async Task SynchronizePointerFileEntries(string[] relativeNames)
        //{
        //    foreach (var relativeName in relativeNames)
        //    {
        //        var (relativeParentPath, directoryName, name) = PointerFileEntryConverter.Deconstruct(relativeName);
        //        var pfe = repo.GetPointerFileEntriesAsync(
        //            options.PointInTimeUtc,
        //            includeDeleted: false,
        //            relativeParentPathEquals: relativeParentPath,
        //            directoryNameEquals: directoryName,
        //            nameEquals: name).ToListAsync();
        //    }
        //}
        */

        async Task Index(DirectoryInfo root)
        {
            foreach (var pfi in fileSystemService.GetPointerFileInfos(root))
            {
                var pf = fileService.GetExistingPointerFile(root, pfi);
                await onIndexedPointerFile(pf);
            }
        }
    }

    async Task Synchronize(RestoreCommandOptions options)
    {
        var existingPointerFileEntries = new HashSet<string>();

        // Get the PointerFiles for the given PointerFileEntries. Create PointerFiles if they do not exist.
        await foreach (var pfe in repo.GetPointerFileEntriesAsync(options.PointInTimeUtc, includeDeleted: false))
        {
            var (_, pf) = fileService.CreatePointerFileIfNotExists(options.Path, pfe);

            logger.LogInformation($"PointerFile '{pf}' created");

            existingPointerFileEntries.Add(pfe.RelativeName);
        }

        // Delete the PointerFiles that do not exist in the given PointerFileEntries.
        foreach (var pfi in fileSystemService.GetPointerFileInfos(options.Path))
        {
            var rn = pfi.GetRelativeName(options.Path);
            if (!existingPointerFileEntries.Contains(rn))
            {
                pfi.Delete();
                logger.LogInformation($"PointerFile '{rn}' deleted");
            }
        }

        options.Path.DeleteEmptySubdirectories();
    }

    async Task Synchronize(RestorePointerFileEntriesCommandOptions options)
    {
        foreach (var relativeName in options.RelativeNames)
        {
            var (relativeParentPath, directoryName, name) = PointerFileEntryConverter.Deconstruct(relativeName);
            var pfe = repo.GetPointerFileEntriesAsync(
                options.PointInTimeUtc,
                includeDeleted: false,
                relativeParentPathEquals: relativeParentPath,
                directoryNameEquals: directoryName,
                nameEquals: name).ToListAsync();
        }
    }







}

internal class DownloadBinaryBlock : ChannelTaskBlockBase<PointerFile>
{
    public DownloadBinaryBlock(ILoggerFactory loggerFactory,
        Func<ChannelReader<PointerFile>> sourceFunc,
        FileService fileService,
        Repository repo,
        IRestoreCommandOptions options,
        Action chunkRehydrating,
        Action onCompleted,
        int maxDegreeOfParallelism)
        : base(loggerFactory: loggerFactory, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, onCompleted: onCompleted)
    {
        this.fileService      = fileService;
        this.repo             = repo;
        this.options          = options;
        this.chunkRehydrating = chunkRehydrating;
    }

    private readonly FileService            fileService;
    private readonly Repository             repo;
    private readonly IRestoreCommandOptions options;
    private readonly Action                 chunkRehydrating;

    private readonly ConcurrentDictionary<BinaryHash, TaskCompletionSource<BinaryFile>> restoredBinaries = new();

    // For unit testing purposes
    internal static bool RestoredFromLocal      { get; set; } = false;
    internal static bool RestoredFromOnlineTier { get; set; } = false;

    protected override async Task ForEachBodyImplAsync(PointerFile pf, CancellationToken ct)
    {
        /* This PointerFile may need to be restored.
         * 
         * 1.   [At the start up the run] The BinaryFile for this PointerFile exists --> no need to restore
         * 2.1. [At the start up the run] The BinaryFile for this PointerFile does not yet exist, and download of the Binary has not started --> start the download
         * 2.2. [At the start up the run] The BinaryFile for this PointerFile does not yet exist, and download of the Binary has started but not completed --> wait for it
         * 2.3. [At the start up the run] The BinaryFile for this PointerFile does not yet exist, and download has completed --> copy
         * 3.   We encounter a restored BinaryFile while we are downloading the same binary?????????????????????????????????????????????????????????
         */

        var binary = await fileService.GetExistingBinaryFileAsync(pf, assertHash: true);
        if (binary is not null)
        {
            // 1. The Binary is already restored
            restoredBinaries.AddOrUpdate(binary.BinaryHash,
                addValueFactory: _ =>
                {
                    logger.LogInformation($"Binary '{binary.RelativeName}' already restored.");
                    
                    var tcs = new TaskCompletionSource<BinaryFile>(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs.SetResult(binary);

                    return tcs;
                },
                updateValueFactory: (bh, tcs) =>
                {
                    /* We are have already processed the hash for this binaryfile, because
                     * 3.1. This pf is a duplicate of a pf2 that was already restored before the run
                     * 3.2. This pf is a duplicate of a pf2 that WAS restored DURING the run
                     * 3.3. This pf is a duplicate of a pf2 that IS currently BEING restored
                     */

                    if (tcs.Task.IsCompleted)
                    {
                        // 3.1 + 3.2: BinaryFile already restored, no need to update the tcs
                        logger.LogInformation($"No need to restore binary for {pf.RelativeName} ('{pf.BinaryHash}') is already restored in '{binary.RelativeName}'");
                    }
                    else
                    {
                        // 3.3
                        logger.LogInformation($"Binary for {pf.RelativeName} ('{pf.BinaryHash}') is being downloaded but we encountered a local duplicate ({binary.RelativeName}). Using that one.");
                        // TODO cancel the ongoing download and use tcs.Task.Result as BinaryFile to copy to pf

                        tcs.SetResult(binary);
                    }

                    return tcs;
                });
        }
        else
        {
            // 2. The Binary is not yet restored

            var binaryToDownload = restoredBinaries.TryAdd(pf.BinaryHash, new TaskCompletionSource<BinaryFile>(TaskCreationOptions.RunContinuationsAsynchronously));
            if (binaryToDownload)
            {
                // 2.1 Download not yet started --> start download
                logger.LogDebug($"Starting download for Binary '{pf.BinaryHash}' ('{pf.RelativeName}')");

                bool restored;
                try
                {
                    restored = await repo.TryDownloadBinaryAsync(pf.BinaryHash, FileSystemService.GetBinaryFileInfo(pf), options.Passphrase);
                }
                catch (Exception e)
                {
                    var e2 = new InvalidOperationException($"Could not restore binary ({pf.BinaryHash}) for {pf.RelativeName}. Delete the PointerFile, disable synchronize and try again to restore without this binary.", e);
                    logger.LogError(e2);

                    throw e2;
                }

                if (restored)
                {
                    RestoredFromOnlineTier = true;
                    binary = await fileService.GetExistingBinaryFileAsync(pf, assertHash: true);
                }
                else
                    chunkRehydrating();

                if (!restoredBinaries[pf.BinaryHash].Task.IsCompleted)
                {
                    restoredBinaries[pf.BinaryHash].SetResult(binary); //also set in case of null (ie not restored because still in Archive tier)
                }
                else
                {
                    // 3.3 while we were downloading this binary, we encountered a local file that had the same hash --> no need to set the binary
                    logger.LogInformation($"We downloaded the binary for {pf.RelativeName} but meanwhile we encountered a local copy already. Download was acutally unnecessary. Sorry.");
                }
            }
            else
            {
                // 2.2 Download ongoing --> wait for it
                binary = await restoredBinaries[pf.BinaryHash].Task;
            }

            if (binary is null)
                //the binary could not yet be restored -- nothing left to do here
                return;

            //TODO what if chunk does not exist?


        }

        var targetBinary = FileSystemService.GetBinaryFileInfo(pf);
        if (!targetBinary.Exists)
        {
            //TODO ensure this path is tested
            
            //The Binary was already restored in another BinaryFile bf (ie this pf is a duplicate) --> copy the bf to this pf
            logger.LogInformation($"Restoring '{pf.RelativeName}' '({pf.BinaryHash})' from '{binary.RelativeName}' to '{targetBinary.FullName}'");
            RestoredFromLocal = true;

            await using (var ss = await binary.OpenReadAsync())
            {
                targetBinary.Directory.Create();
                await using var ts = File.OpenWrite(targetBinary.FullName);
                await ss.CopyToAsync(ts); // File.Copy keeps the file locked when we re setting CreationTime and LastWriteTime
            }
        }

        targetBinary.CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName);
        targetBinary.LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName);

        if (!options.KeepPointers)
            pf.Delete();
    }
}