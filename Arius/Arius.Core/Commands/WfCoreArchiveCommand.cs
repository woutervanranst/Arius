using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    internal class WfCoreArchiveCommand : ICommand
    {
        internal interface IOptions
        {
            string Path { get; }
        }

        public WfCoreArchiveCommand(IOptions options,
            ILogger<ArchiveCommand> logger,
            IServiceProvider serviceProvider)
        {
            root = new DirectoryInfo(options.Path);
            this._logger = logger;
            this.services = serviceProvider;
        }

        private readonly DirectoryInfo root;
        private readonly ILogger<ArchiveCommand> _logger;
        private readonly IServiceProvider services;

        internal static void AddBlockProviders(IServiceCollection coll/*, Facade.Facade.ArchiveCommandOptions options*/)
        {
            //coll
            //.AddWorkflow()

            //.AddTransient<IndexDirectoryStep>()
            //.AddTransient<AddHashStep>()

            //services.AddWorkflow(x => x.UseMongoDB(@"mongodb://localhost:27017", "workflow"));
            ;
        }

        IServiceProvider ICommand.Services => throw new NotImplementedException();


        public async Task<int> Execute()
        {
            var indexedFiles = new BlockingCollection<IFile>();

            var indexBlock = new IndexBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<IndexBlock>(),
                root: root,
                indexedFile: (file) => indexedFiles.Add(file),
                done: () => indexedFiles.CompleteAdding());
            var indexTask = indexBlock.GetTask;


            var createPointerFileEntry = new BlockingCollection<PointerFile>();
            var createManifest = new BlockingCollection<BinaryFile>();

            var hashBlock = new HashBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<HashBlock>(),
                continueWhile: () => !indexedFiles.IsCompleted,
                source: indexedFiles.GetConsumingPartitioner(),
                maxDegreeOfParallelism: 2 /*Environment.ProcessorCount */,
                hashedPointerFile: (pf) => createPointerFileEntry.Add(pf),
                hashedBinaryFile: (bf) => createManifest.Add(bf),
                hvp: services.GetRequiredService<IHashValueProvider>(),
                done: () =>
                {
                    createManifest.CompleteAdding();
                    createPointerFileEntry.CompleteAdding();
                });
            var hashTask = hashBlock.GetTask;


            var binariesToChunk = new BlockingCollection<BinaryFile>();
            var waitPipe = new ConcurrentDictionary<HashValue, ConcurrentBag<BinaryFile>>();
            var pointersToCreate = new BlockingCollectionEx<BinaryFile>();
            //var pointersToCreateDone = new AsyncManualResetEvent(); // new Mutex(); // SemaphoreSlim(1);

            var processHashedBinaryBlock = new ProcessHashedBinaryBlock(
                logger: services.GetRequiredService<ILoggerFactory>().CreateLogger<ProcessHashedBinaryBlock>(),
                continueWhile: () => !createManifest.IsCompleted,
                source: createManifest.GetConsumingEnumerable(),
                repo: services.GetRequiredService<AzureRepository>(),
                uploadBinaryFile: (bf) => binariesToChunk.Add(bf),  //B401
                waitForCreatedManifest: (bf) =>
                {
                    waitPipe.AddOrUpdate(
                        key: bf.Hash,
                        addValue: new() { bf },
                        updateValueFactory: (h, bag) =>
                        {
                            bag.Add(bf);
                            return bag;
                        });
                }, //B402
                manifestExists: (bf) => pointersToCreate.Add(bf), //B403
                done: () =>
                {
                    binariesToChunk.CompleteAdding();
                    //pointersToCreateDone.Set();
                    //pointersToCreate.CompleteAdding(); NIET HIER

                });
            var processHashedBinaryTask = processHashedBinaryBlock.GetTask;



            var chunkBlock = new ChunkBlock(
                logger: services.GetRequiredService<ChunkBlock>().CreateLogger<ProcessHashedBinaryBlock>(),
                );
            var chunkTask = chunkBlock.GetTask;



            await Task.WhenAll(pointersToCreate.WaitAddingCompleted);

            await Task.WhenAll(BlockBase.AllTasks);

            return 0;
        }
    }
    

    internal class IndexBlock : SingleTaskBlockBase
    {
        public IndexBlock(ILogger<IndexBlock> logger, DirectoryInfo root, Action<IFile> indexedFile, Action done) 
            : base(
                  continueWhile: () => false, //no not keep running after the directory is indexed
                  done: done)
        {
            this.logger = logger;
            this.root = root;
            this.indexedFile = indexedFile;
        }

        private readonly ILogger<IndexBlock> logger;
        private readonly DirectoryInfo root;
        private readonly Action<IFile> indexedFile;
        
        protected override void TaskBodyImpl()
        {
            foreach (var file in IndexDirectory(root))
                indexedFile(file);
        }


        private IEnumerable<IFile> IndexDirectory(DirectoryInfo directory) => IndexDirectory(directory, directory);
        private IEnumerable<IFile> IndexDirectory(DirectoryInfo root, DirectoryInfo directory)
        {
            foreach (var file in directory.GetFiles())
            {
                if (IsHiddenOrSystem(file))
                {
                    logger.LogDebug($"Skipping file {file.FullName} as it is SYSTEM or HIDDEN");
                    continue;
                }
                else if (IsIgnoreFile(file))
                {
                    logger.LogDebug($"Ignoring file {file.FullName}");
                    continue;
                }
                else
                {
                    yield return GetFile(root, file);
                }
            }

            foreach (var dir in directory.GetDirectories())
            {
                if (IsHiddenOrSystem(dir))
                {
                    logger.LogDebug($"Skipping directory {dir.FullName} as it is SYSTEM or HIDDEN");
                    continue;
                }

                foreach (var f in IndexDirectory(root, dir))
                    yield return f;
            }
        }
        private bool IsHiddenOrSystem(DirectoryInfo d)
        {
            if (d.Name == "@eaDir") //synology internals -- ignore
                return true;

            return IsHiddenOrSystem(d.Attributes);

        }
        private bool IsHiddenOrSystem(FileInfo fi)
        {
            if (fi.FullName.Contains("eaDir") ||
                fi.FullName.Contains("SynoResource"))
                //fi.FullName.Contains("@")) // commenting out -- email adresses are not weird
                logger.LogWarning("WEIRD FILE: " + fi.FullName);

            return IsHiddenOrSystem(fi.Attributes);
        }
        private static bool IsHiddenOrSystem(FileAttributes attr)
        {
            return (attr & FileAttributes.System) != 0 || (attr & FileAttributes.Hidden) != 0;
        }
        private static bool IsIgnoreFile(FileInfo fi)
        {
            var lowercaseFilename = fi.Name.ToLower();

            return lowercaseFilename.Equals("autorun.ini") ||
                lowercaseFilename.Equals("thumbs.db") ||
                lowercaseFilename.Equals(".ds_store");
        }
        private IFile GetFile(DirectoryInfo root, FileInfo fi)
        {
            if (fi.IsPointerFile())
            {
                logger.LogInformation($"Found PointerFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

                return new PointerFile(root, fi);
            }
            else
            {
                logger.LogInformation($"Found BinaryFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

                return new BinaryFile(root, fi);
            }
        }
    }


    internal class HashBlock : MultiThreadForEachTaskBlockBase<IFile>
    {
        public HashBlock(ILogger<HashBlock> logger,
            Func<bool> continueWhile,
            Partitioner<IFile> source, 
            int maxDegreeOfParallelism,
            Action<PointerFile> hashedPointerFile,
            Action<BinaryFile> hashedBinaryFile,
            IHashValueProvider hvp,
            Action done) : base(continueWhile, done)
        {
            this.logger = logger;
            this.source = source;
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.hashedPointerFile = hashedPointerFile;
            this.hashedBinaryFile = hashedBinaryFile;
            this.hvp = hvp;
        }

        private readonly ILogger<HashBlock> logger;
        private readonly Partitioner<IFile> source;
        private readonly int maxDegreeOfParallelism;
        private readonly Action<PointerFile> hashedPointerFile;
        private readonly Action<BinaryFile> hashedBinaryFile;
        private readonly IHashValueProvider hvp;

        protected override Partitioner<IFile> Source => source;
        protected override int MaxDegreeOfParallelism => maxDegreeOfParallelism;
        protected override void ForEachBodyImpl(IFile item)
        {
            if (item is PointerFile pf)
            {
                // A pointerfile already knows its hash
                hashedPointerFile(pf);
            }
            else if (item is BinaryFile bf)
            {
                logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId}] Hashing BinaryFile {bf.RelativeName}");

                // For BinaryFiles we need to calculate it
                bf.Hash = hvp.GetHashValue(bf);

                logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId}] Hashing BinaryFile {bf.RelativeName} done");

                hashedBinaryFile(bf);
            }
            else
                throw new ArgumentException($"Cannot add hash to item of type {item.GetType().Name}");

            //Thread.Sleep(1000);
            //await Task.Delay(4000);
        }
    }


    internal class ProcessHashedBinaryBlock : SingleThreadForEachTaskBlockBase<BinaryFile>
    {
        public ProcessHashedBinaryBlock(ILogger<ProcessHashedBinaryBlock> logger,
           Func<bool> continueWhile,
           IEnumerable<BinaryFile> source,
           AzureRepository repo,
           Action<BinaryFile> uploadBinaryFile,
           Action<BinaryFile> waitForCreatedManifest,
           Action<BinaryFile> manifestExists,
           Action done) : base(source, continueWhile, done)
        {
            this.logger = logger;
            this.repo = repo;
            this.uploadBinaryFile = uploadBinaryFile;
            this.waitForCreatedManifest = waitForCreatedManifest;
            this.manifestExists = manifestExists;
        }

        private readonly ILogger<ProcessHashedBinaryBlock> logger;
        private readonly AzureRepository repo;
        private readonly Action<BinaryFile> uploadBinaryFile;
        private readonly Action<BinaryFile> waitForCreatedManifest;
        private readonly Action<BinaryFile> manifestExists;

        protected override async Task ForEachBodyImplAsync(BinaryFile item)
        {
            /* 
             * Three possibilities:
             *      1. BinaryFile arrives, remote manifest already exists --> send to next block //TODO explain WHY
             *      2. BinaryFile arrives, remote manifest does not exist and is not being created --> send to the creation pipe
             *      3. BinaryFile arrives, remote manifest does not exist and IS beign created --> add to the waiting pipe
             */

            //lock (created) // lock because created can be modified by the 'file uploaded' handler on another thread
            //{
                lock (creating) // TODO WHY double locking?
                {
                    if (ManifestExists(item.Hash))
                    {
                        // 1 - Exists remote
                        logger.LogInformation($"Manifest for hash of BinaryFile {item.Name} already exists. No need to upload.");

                        manifestExists(item);
                    }
                    else if (!creating.Contains(item.Hash))
                    {
                        // 2 Does not yet exist remote and not yet being created --> upload
                        logger.LogInformation($"Manifest for hash of BinaryFile {item.Name} does not exist remotely. To upload and create pointer.");

                        creating.Add(item.Hash);
                        uploadBinaryFile(item);
                        waitForCreatedManifest(item);
                    }
                    else
                    {
                        // 3 Does not exist remote but is being created
                        logger.LogInformation($"Manifest for hash of BinaryFile {item.Name} does not exist remotely but is already being uploaded. To wait and create pointer.");

                        waitForCreatedManifest(item);
                    }
                }
            //}
        }

        private readonly List<HashValue> creating = new();

        private bool ManifestExists(HashValue h)
        {
            //// Check cache
            //if (created.ContainsKey(h))
            //    return created[h];

            // Check remote
            var e = repo.ManifestExistsAsync(h).Result;
            //created.Add(h, e); //Add result to cache so we dont need to recheck again next time

            return e;
        }
        //private readonly Dictionary<HashValue, bool> created = new();
    }

    internal class ChunkBlock : MultiThreadForEachTaskBlockBase<BinaryFile>
    {
        public ChunkBlock(ILogger<ChunkBlock> logger,
            Func<bool> continueWhile,
            Partitioner<BinaryFile> source,
            int maxDegreeOfParallelism,
            IChunker chunker, 
            AzureRepository azureRepository,
            //Action<PointerFile> hashedPointerFile,
            //Action<BinaryFile> hashedBinaryFile,
            //IHashValueProvider hvp,
            Action done) : base(continueWhile, done)
        {
            this.logger = logger;
            this.source = source;
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.chunker = chunker;
            this.azureRepository = azureRepository;
        }

        private readonly ILogger<ChunkBlock> logger;
        private readonly Partitioner<BinaryFile> source;
        private readonly int maxDegreeOfParallelism;
        private readonly IChunker chunker;
        private readonly AzureRepository azureRepository;

        protected override Partitioner<BinaryFile> Source => source;

        protected override int MaxDegreeOfParallelism => maxDegreeOfParallelism;

        protected override void ForEachBodyImpl(BinaryFile item)
        {
            throw new NotImplementedException();
        }
    }
}
