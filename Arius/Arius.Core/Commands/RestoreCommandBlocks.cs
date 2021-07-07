using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Restore
{
    internal class IndexBlock : TaskBlockBase<FileSystemInfo>
    {
        public IndexBlock(ILogger<IndexBlock> logger,
            Func<FileSystemInfo> sourceFunc,
            int maxDegreeOfParallelism,
            bool synchronize,
            Repository repo,
            PointerService pointerService,
            Action<(PointerFile PointerFile, bool AlreadyRestored)> pointerToDownload,
            Action done)
            : base(logger: logger, sourceFunc: sourceFunc, done: done)
        {
            this.synchronize = synchronize;
            this.repo = repo;
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.pointerService = pointerService;
            this.pointerToDownload = pointerToDownload;
        }

        private readonly bool synchronize;
        private readonly Repository repo;
        private readonly int maxDegreeOfParallelism;
        private readonly PointerService pointerService;
        private readonly Action<(PointerFile PointerFile, bool AlreadyRestored)> pointerToDownload;

        protected override async Task TaskBodyImplAsync(FileSystemInfo fsi)
        {
            if (synchronize)
            {
                if (fsi is DirectoryInfo root)
                {
                    // Synchronize a Directory
                    var currentPfes = (await repo.GetCurrentEntries(includeDeleted: false)).ToArray();

                    logger.LogInformation($"{currentPfes.Length} files in latest version of remote");

                    var t1 = Task.Run(() => CreatePointerFilesIfNotExist(root, currentPfes));
                    var t2 = Task.Run(() => DeletePointerFilesIfShouldNotExist(root, currentPfes));

                    await Task.WhenAll(t1, t2);
                }
                else if (fsi is FileInfo)
                    throw new InvalidOperationException($"The synchronize flag is not valid when the path is a file"); //TODO UNIT TEST
                else
                    throw new InvalidOperationException($"The synchronize flag is not valid with argument {fsi}");
            }
            else
            {
                if (fsi is DirectoryInfo root)
                    ProcessPointersInDirectory(root);
                else if (fsi is FileInfo fi && fi.IsPointerFile())
                    ProcessPointerFile(new PointerFile(fi.Directory, fi)); //TODO test dit in non root
                else
                    throw new InvalidOperationException($"Argument {fsi} is not valid");
            }
        }

        /// <summary>
        /// Get the PointerFiles for the given PointerFileEntries. Create PointerFiles if they do not exist.
        /// </summary>
        /// <returns></returns>
        private void CreatePointerFilesIfNotExist(DirectoryInfo root, PointerFileEntry[] pfes)
        {
            foreach (var pfe in pfes
                                    .AsParallel()
                                    .WithDegreeOfParallelism(maxDegreeOfParallelism))
            {
                var pf = pointerService.CreatePointerFileIfNotExists(root, pfe);
                ProcessPointerFile(pf);
            }
        }

        /// <summary>
        /// Delete the PointerFiles that do not exist in the given PointerFileEntries.
        /// </summary>
        /// <param name="pfes"></param>
        private void DeletePointerFilesIfShouldNotExist(DirectoryInfo root, PointerFileEntry[] pfes)
        {
            var relativeNames = pfes.Select(pfe => pfe.RelativeName).ToArray();

            foreach (var pfi in root.GetPointerFileInfos()
                                        .AsParallel()
                                        .WithDegreeOfParallelism(maxDegreeOfParallelism))
            {
                var relativeName = pfi.GetRelativePath(root);

                if (relativeNames.Contains(relativeName))
                    return;

                pfi.Delete();
                logger.LogInformation($"Pointer for '{relativeName}' deleted");
            }

            root.DeleteEmptySubdirectories();
        }

        private void ProcessPointersInDirectory(DirectoryInfo root)
        {
            var pfs = root.GetPointerFileInfos().Select(fi => new PointerFile(root, fi));

            foreach (var pf in pfs)
                ProcessPointerFile(pf);
        }

        private void ProcessPointerFile(PointerFile pf)
        {
            var bf = pointerService.GetBinaryFile(pf, ensureCorrectHash: true);

            pointerToDownload((pf, bf is not null));
        }
    }

    internal class ProcessPointerFileBlock : BlockingCollectionTaskBlockBase<PointerFile>
    {
        public ProcessPointerFileBlock(ILogger<ProcessPointerFileBlock> logger,
            Func<BlockingCollection<PointerFile>> sourceFunc,
            Repository repo,
            PointerService pointerService,
            Action<PointerFile, BinaryFile> alreadyRestored,
            Action done)
            : base(logger: logger, sourceFunc: sourceFunc, done: done)
        {
            this.repo = repo;
            this.pointerService = pointerService;
            this.alreadyRestored = alreadyRestored;
        }
        
        private readonly Repository repo;
        private readonly PointerService pointerService;
        private readonly Action<PointerFile, BinaryFile> alreadyRestored;

        private readonly ConcurrentBag<ManifestHash> restoredOrRestoring = new();


        protected override Task ForEachBodyImplAsync(PointerFile pf)
        {
            //if (pointerService.GetBinaryFile(pf, ensureCorrectHash: true) is BinaryFile bf &&
            //    bf is not null) //NOTE: we are deliberatebly not checking whether this PointerFile is already in restoredOrRestoring -- from the PoV of this block, this doesnt matter
            //{
            //    //This PointerFile already has a restored BinaryFile
            //    if (!restoredOrRestoring.Contains(bf.Hash))
            //        restoredOrRestoring.Add(bf.Hash);
            //    alreadyRestored(pf, bf); 

            //}


            return Task.CompletedTask;
        }

    }
}

