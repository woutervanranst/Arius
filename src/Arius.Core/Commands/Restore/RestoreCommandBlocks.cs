﻿using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Arius.Core.Commands.Restore;

internal class ProvisionPointerFilesBlock : TaskBlockBase<DirectoryInfo>
{
    public ProvisionPointerFilesBlock(ILoggerFactory loggerFactory,
       Func<DirectoryInfo> sourceFunc,
       int maxDegreeOfParallelism,
       RestoreCommandOptions options,
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
    private readonly RestoreCommandOptions   options;
    private readonly Repository              repo;
    private readonly FileSystemService       fileSystemService;
    private readonly FileService             fileService;
    private readonly Func<PointerFile, Task> onIndexedPointerFile;

    protected override async Task TaskBodyImplAsync(DirectoryInfo root)
    {
        var pointerFiles = AsyncEnumerable.Empty<PointerFile>();

        if (options.Synchronize)
        {
            switch (options)
            {
                case RestorePointerFileEntriesCommandOptions: // put the more specific type first
                    throw new NotSupportedException("We cannot synchronize PointerFiles when we are only restoring a select number of relativeNames.");
                case RestoreCommandOptions o:
                    pointerFiles = SynchronizePointerFilesAsync(o);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        else
        {
            switch (options)
            {
                case RestorePointerFileEntriesCommandOptions o:
                    pointerFiles = CreatePointerFilesAsync(o);
                    break;
                case RestoreCommandOptions o:
                    pointerFiles = GetExistingPointerFiles(o);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        await foreach (var pf in pointerFiles)
            await onIndexedPointerFile(pf);
    }

    /// <summary>
    /// Create the PointerFiles (if they do not exist) for the given root
    /// Delete the PointerFiles that should not exist
    /// </summary>
    private async IAsyncEnumerable<PointerFile> SynchronizePointerFilesAsync(RestoreCommandOptions options)
    {
        var existingPointerFileEntries = new HashSet<string>();
        
        await foreach (var pfe in repo.GetPointerFileEntriesAsync(options.PointInTimeUtc, includeDeleted: false))
        {
            var (_, pf) = fileService.CreatePointerFileIfNotExists(options.Path, pfe);

            logger.LogInformation($"PointerFile '{pf}' created");
            yield return pf;

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

    /// <summary>
    /// Create the PointerFiles (if they do not exist) for the given relativeNames
    /// </summary>
    private async IAsyncEnumerable<PointerFile> CreatePointerFilesAsync(RestorePointerFileEntriesCommandOptions options)
    {
        foreach (var relativeName in options.RelativeNames)
        {
            var pfe = await repo.GetPointerFileEntriesAsync(
                options.PointInTimeUtc,
                includeDeleted: false,
                relativeDirectory: relativeName).SingleAsync();

            var (_, pf) = fileService.CreatePointerFileIfNotExists(options.Path, pfe);

            logger.LogInformation($"PointerFile '{pf}' created");
            yield return pf;
        }
    }

    private async IAsyncEnumerable<PointerFile> GetExistingPointerFiles(RestoreCommandOptions options)
    {
        foreach (var pfi in fileSystemService.GetPointerFileInfos(options.Path))
        {
            var pf = fileService.GetExistingPointerFile(options.Path, pfi);
            yield return pf;
        }
    }
}

internal class DownloadBinaryBlock : ChannelTaskBlockBase<PointerFile>
{
    public DownloadBinaryBlock(ILoggerFactory loggerFactory,
        Func<ChannelReader<PointerFile>> sourceFunc,
        FileService fileService,
        Repository repo,
        RestoreCommandOptions options,
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

    private readonly FileService           fileService;
    private readonly Repository            repo;
    private readonly RestoreCommandOptions options;
    private readonly Action                chunkRehydrating;

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