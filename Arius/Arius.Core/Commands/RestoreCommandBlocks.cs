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
        private void CreateIfNotExists(IEnumerable<PointerFileEntry> pfes)
        {
            pfes
                .AsParallel()
                .ForAll(pfe =>
                {
                    var pf = pointerService.CreatePointerFileIfNotExists(pfe);
                    pointerToDownload(pf);
                });
        }

        /// <summary>
        /// Delete the PointerFiles that do not exist in the given PointerFileEntries.
        /// </summary>
        /// <param name="pfes"></param>
        private void DeleteIfExists(DirectoryInfo root, IEnumerable<PointerFileEntry> pfes)
        {
            var relativeNames = pfes.Select(pfe => pfe.RelativeName).ToArray();

            root.GetFiles($"*{PointerFile.Extension}", SearchOption.AllDirectories)
                .AsParallel()
                .ForAll(pfi =>
                {
                    var relativeName = Path.GetRelativePath(root.FullName, pfi.FullName);

                    if (relativeNames.Contains(relativeName))
                        return;
                    pfi.Delete();
                    logger.LogInformation($"Pointer for '{relativeName}' deleted");
                });

            root.DeleteEmptySubdirectories();
        }
    }


}

