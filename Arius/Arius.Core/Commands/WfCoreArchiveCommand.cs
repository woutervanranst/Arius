using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            var hashedFiles = new BlockingCollection<FileBase>();


            var createPointerFileEntryIfNotExistsInput = new BlockingCollection<PointerFile>();
            var createIfNotExistManifestInput = new BlockingCollection<BinaryFile>();


            var indexBlock = new IndexBlock(
                root: root, 
                indexedFile: (file) => indexedFiles.Add(file),
                done: () => indexedFiles.CompleteAdding());
            var indexTask = indexBlock.GetTask;


            var hashBlock = new HashBlock(
                logger: services.GetRequiredService<LoggerFactory>().CreateLogger<HashBlock>(),
                okToStop: () => !indexedFiles.IsCompleted,
                source: indexedFiles.GetConsumingPartitioner(),
                maxDegreeOfParallelism: 2 /*Environment.ProcessorCount */,
                hashedPointerFile: (file) => hashedFiles.Add(file),
                hvp: services.GetRequiredService<IHashValueProvider>());
            var hashTask = hashBlock.GetTask;


            var s = Task.Run(() =>
            {
                while (!hashedFiles.IsCompleted)
                {
                    foreach (var file in hashedFiles.GetConsumingEnumerable())
                    {
                        if (file is PointerFile pf)
                            createPointerFileEntryIfNotExistsInput.Add(pf);
                        else if (file is BinaryFile bf)
                            createIfNotExistManifestInput.Add(bf);
                        else
                            throw new InvalidOperationException();
                    }

                    createPointerFileEntryIfNotExistsInput.CompleteAdding();
                    createIfNotExistManifestInput.CompleteAdding();
            });


            await Task.WhenAll(indexTask, hashTask, s);

            return 0;
        }



        

    }

    internal abstract class BlockBase
    {
        public abstract Task GetTask { get; }
    }

    internal abstract class SingleTaskBlockBase : BlockBase
    {
        protected abstract void BodyImpl();
        public override sealed Task GetTask => Task.Run(() => BodyImpl());
    }

    internal abstract class MultiTaskBlockBase<TSource> : BlockBase
    {
        protected abstract bool OkToStop { get; }
        protected abstract Partitioner<TSource> Source { get; }
        protected abstract int MaxDegreeOfParallelism { get; }
        protected abstract void BodyImpl(TSource item);

        public override sealed Task GetTask
        {
            get
            {
                return Task.Run(() =>
                {
                    Parallel.ForEach(
                        Source,
                        new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism },
                        item => BodyImpl(item));
                });
            }
        }
    }

    internal class IndexBlock : SingleTaskBlockBase
    {
        public IndexBlock(DirectoryInfo root, Action<IFile> indexedFile, Action done)
        {
            this.root = root;
            this.indexedFile = indexedFile;
            this.done = done;
        }

        private readonly DirectoryInfo root;
        private readonly Action<IFile> indexedFile;
        private readonly Action done;

        protected override void BodyImpl()
        {
            foreach (var file in IndexDirectory(root))
                indexedFile(file);

            done();
        }


        private IEnumerable<IFile> IndexDirectory(DirectoryInfo directory) => IndexDirectory(directory, directory);
        private IEnumerable<IFile> IndexDirectory(DirectoryInfo root, DirectoryInfo directory)
        {
            foreach (var file in directory.GetFiles())
            {
                if (IsHiddenOrSystem(file))
                {
                    _logger.LogDebug($"Skipping file {file.FullName} as it is SYSTEM or HIDDEN");
                    continue;
                }
                else if (IsIgnoreFile(file))
                {
                    _logger.LogDebug($"Ignoring file {file.FullName}");
                    continue;
                }
                else
                {
                    yield return GetAriusEntry(root, file);
                }
            }

            foreach (var dir in directory.GetDirectories())
            {
                if (IsHiddenOrSystem(dir))
                {
                    _logger.LogDebug($"Skipping directory {dir.FullName} as it is SYSTEM or HIDDEN");
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
                _logger.LogWarning("WEIRD FILE: " + fi.FullName);

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
        private IFile GetAriusEntry(DirectoryInfo root, FileInfo fi)
        {
            if (fi.IsPointerFile())
            {
                _logger.LogInformation($"Found PointerFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

                return new PointerFile(root, fi);
            }
            else
            {
                _logger.LogInformation($"Found BinaryFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

                return new BinaryFile(root, fi);
            }
        }
    }

    internal class HashBlock : MultiTaskBlockBase<IFile>
    {
        public HashBlock(ILogger<HashBlock> logger,
            Func<bool> okToStop,
            Partitioner<IFile> source, 
            int maxDegreeOfParallelism,
            Action<PointerFile> hashedPointerFile,
            Action<BinaryFile> hashedBinaryFile,
            IHashValueProvider hvp)
        {
            this.logger = logger;
            this.okToStop = okToStop;
            this.source = source;
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.hashedPointerFile = hashedPointerFile;
            this.hashedBinaryFile = hashedBinaryFile;
            this.hvp = hvp;
        }

        private readonly ILogger<HashBlock> logger;
        private readonly Func<bool> okToStop;
        private readonly Partitioner<IFile> source;
        private readonly int maxDegreeOfParallelism;
        private readonly Action<PointerFile> hashedPointerFile;
        private readonly Action<BinaryFile> hashedBinaryFile;
        private readonly IHashValueProvider hvp;

        protected override bool OkToStop => okToStop();
        protected override Partitioner<IFile> Source => source;
        protected override int MaxDegreeOfParallelism => maxDegreeOfParallelism;
        protected override void BodyImpl(IFile item)
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
}
