using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands
{
    internal class RestoreCommand : ICommand //This class is internal but the interface is public for use in the Facade
    {
        internal interface IOptions
        {
            string Path { get; }
            bool Download { get; }
            bool Synchronize { get; }
        }

        public RestoreCommand(IOptions options,
            ILogger<RestoreCommand> logger,
            IServiceProvider serviceProvider)
        {
            this.options = options;
            this.logger = logger;
            this.services = serviceProvider;
        }

        private readonly IOptions options;
        private readonly ILogger<RestoreCommand> logger;
        private readonly IServiceProvider services;

        //internal static void AddBlockProviders(IServiceCollection coll)
        //{
        //}

        IServiceProvider ICommand.Services => services;

        public async Task<int> Execute()
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var repo = services.GetRequiredService<Repository>();
            var pointerService = services.GetRequiredService<PointerService>();
            object path = File.GetAttributes(options.Path).HasFlag(FileAttributes.Directory) ?
                new DirectoryInfo(options.Path) :
                new FileInfo(options.Path);


            var directoriesToSynchronize = new BlockingCollection<DirectoryInfo>();
            var pointerFilesToDownload = new BlockingCollection<PointerFile>();

            // Start the flow
            if (options.Synchronize)
            {
                if (path is DirectoryInfo root)
                {
                    // Synchronize a Directory
                    directoriesToSynchronize.Add(root); //S10
                    directoriesToSynchronize.CompleteAdding(); //S11
                }
                else
                {
                    throw new InvalidOperationException("The synchronize flag is not valid when the path is a file");
                }
            }
            else if (options.Download)
            {
                directoriesToSynchronize.CompleteAdding(); //S12

                if (path is DirectoryInfo root)
                {
                    var pfs = root.GetPointerFileInfos().Select(fi => new PointerFile(root, fi));
                    pointerFilesToDownload.AddFromEnumerable(pfs, completeAddingWhenDone: true); //S13
                }
                else if (path is FileInfo fi)
                {
                    var pf = new PointerFile(null, fi);
                    pointerFilesToDownload.Add(pf); //S14     //TODO test dit in een NON ROOT!
                    pointerFilesToDownload.CompleteAdding(); //S15
                }
            }


            var synchronizeBlock = new SynchronizeBlock(
                logger: loggerFactory.CreateLogger<SynchronizeBlock>(),
                source: directoriesToSynchronize,
                repo: repo,
                pointerService: pointerService,
                pointerToDownload: pf =>
                {
                    if (options.Download) //S21
                        pointerFilesToDownload.Add(pf);
                },
                done: () => { });
            var synchronizeTask = synchronizeBlock.GetTask;







            


            


            await Task.WhenAny(Task.WhenAll(BlockBase.AllTasks), BlockBase.CancellationTask);



            /*
            //var root = new DirectoryInfo(_options.Path);
            //if (root.Exists && root.EnumerateFiles().Any())
            //{
            //    // TODO use !pf.LocalContentFileInfo.Exists 
            //    _logger.LogWarning("The folder is not empty. There may be lingering files after the restore.");
            //    //TODO LOG WARNING if local root directory contains other things than the pointers with their respecitve localcontentfiles --> will not be overwritten but may be backed up
            //}

            //var dedupedItems = _azureRepository.GetAllManifestHashes()
            //    .SelectMany(manifestHash => _azureRepository
            //        .GetChunkHashesAsync(manifestHash).Result
            //            .Select(chunkHash => new { manifestHash, chunkHash }))
            //    .GroupBy(zz => zz.chunkHash)
            //    .Where(kk => kk.Count() > 1)
            //    .ToList();

            //var orphanedChunksWithoutPointers = _azureRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).Except(_azureRepository.GetCurrentEntries(true).Select(z => z.ManifestHash)).ToArray();
            //var orphanedChunksWithoutManifest_Cannotberestored = _azureRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).Except(_azureRepository.GetAllManifestHashes()).ToArray();

            //var zz = _azureRepository.GetCurrentEntries(true).ToArray();

            //var ozz = zz.Where(a => orphanedChunksWithoutPointers.Contains(a.ManifestHash));
            */





            return 0;




        }
    }
}
