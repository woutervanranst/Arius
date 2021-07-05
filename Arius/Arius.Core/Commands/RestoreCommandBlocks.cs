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

namespace Arius.Core.Commands
{
    internal class SynchronizeBlock2 : TaskBlockBase<DirectoryInfo>
    {
        public IndexBlock(
            ILogger<SynchronizeBlock2> logger,
            DirectoryInfo root,
            int maxDegreeOfParallelism,
            //bool fastHash,
            Repository repo,
            //Action<PointerFile> hashedPointerFile,
            //Action<BinaryFile> hashedBinaryFile,
            //Action<BinaryFile> binaryFileAlreadyBackedUp,
            //IHashValueProvider hvp,
            Action done)
            : base(logger: logger, source: root, done: done)
        {
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.repo = repo;
        }

        private readonly int maxDegreeOfParallelism;
        private readonly Repository repo;

        protected override async Task TaskBodyImplAsync(DirectoryInfo source)
        {
            var currentPfes = (await repo.GetCurrentEntries(includeDeleted: false)).ToArray();
            foreach (var pfe in currentPfes)
            {

            }


            foreach (var item in source.GetPointerFileInfos()
                                        .AsParallel()
                                        .WithDegreeOfParallelism(maxDegreeOfParallelism))
            {

            }

            return Task.CompletedTask;
        }
    }


    internal class SynchronizeBlock : BlockingCollectionTaskBlockBase<DirectoryInfo>
    {
        public SynchronizeBlock(ILogger<SynchronizeBlock> logger,
            BlockingCollection<DirectoryInfo> source,
            Repository repo,
            PointerService pointerService,
            Action<PointerFile> pointerToDownload,
            Action done)
            : base(logger: logger, source: source, done: done)
        {
            this.repo = repo;
            this.pointerService = pointerService;
            this.pointerToDownload = pointerToDownload;
        }

        private readonly Repository repo;
        private readonly PointerService pointerService;
        private readonly Action<PointerFile> pointerToDownload;

        protected override async Task ForEachBodyImplAsync(DirectoryInfo root)
        {
            var currentPfes = (await repo.GetCurrentEntries(includeDeleted: false)).ToArray();

            logger.LogInformation($"{currentPfes.Count()} files in latest version of remote");

            var t1 = Task.Run(() => CreateIfNotExists(currentPfes));
            var t2 = Task.Run(() => DeleteIfExists(root, currentPfes));

            await Task.WhenAll(t1, t2);
        }

        /// <summary>
        /// Get the PointerFiles for the given PointerFileEntries. Create PointerFiles if they do not exist.
        /// </summary>
        /// <returns></returns>
        private void CreateIfNotExists(PointerFileEntry[] pfes)
        {
            foreach (var pfe in pfes.AsParallel())
            {
                var pf = pointerService.CreatePointerFileIfNotExists(pfe);
                pointerToDownload(pf);
            }
        }

        /// <summary>
        /// Delete the PointerFiles that do not exist in the given PointerFileEntries.
        /// </summary>
        /// <param name="pfes"></param>
        private void DeleteIfExists(DirectoryInfo root, PointerFileEntry[] pfes)
        {
            var relativeNames = pfes.Select(pfe => pfe.RelativeName).ToArray();

            foreach (var pfi in root.GetPointerFileInfos().AsParallel())
            {
                var relativeName = pfi.GetRelativePath(root);

                if (relativeNames.Contains(relativeName))
                    return;

                pfi.Delete();
                logger.LogInformation($"Pointer for '{relativeName}' deleted");
            }

            root.DeleteEmptySubdirectories();
        }
    }

    internal class ProcessPointerFileBlock : BlockingCollectionTaskBlockBase<PointerFile>
    {
        public ProcessPointerFileBlock(ILogger<ProcessPointerFileBlock> logger,
            BlockingCollection<PointerFile> source,
            Repository repo,
            PointerService pointerService,
            Action<PointerFile, BinaryFile> alreadyRestored,
            Action done)
            : base(logger: logger, source: source, done: done)
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

