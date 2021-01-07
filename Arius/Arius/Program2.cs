using Arius.Services;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Arius.Models;

namespace Arius
{
    // https://www.c-sharpcorner.com/article/create-a-long-running-task-in-c-sharp-net-core/

    internal abstract class ProcessStep<TIn, TOut>
    {
        private readonly CancellationTokenSource cts = new ();
        //private readonly AutoResetEvent are = new(false);

        private Task workerTask;


        protected ProcessStep()
        {
            StartWork();
        }

        private readonly ConcurrentQueue<TIn> _queue = new();

        public void Enqueue(TIn item)
        {
            _queue.Enqueue(item);
            //are.Set();
        }

        private void StartWork()
        {
            workerTask = Task.Run(async () =>
            {
                Thread.CurrentThread.Name = this.GetType().Name;

                while (!cts.IsCancellationRequested)
                {
                    //are.WaitOne();

                    while (_queue.IsEmpty)
                        await Task.Yield();

                    _queue.TryDequeue(out var wi1);

                    Task.Run(() =>
                    {
                        foreach (var wi2 in Work(wi1).AsParallel())
                            NextAction(wi2);
                    });
                }
            });
        }

        public abstract IEnumerable<TOut> Work(TIn workItem);

        public Action<TOut> NextAction { get; set; }

        public void CancelTask()
        {
            cts.Cancel();
        }

        public Task WorkerTask => workerTask;
    }




    internal abstract class AriusArchiveItem
    {
        private readonly FileInfo _fi;

        protected AriusArchiveItem(FileInfo fi)
        {
            _fi = fi;
        }

        public string FileFullName => _fi.FullName;
        public string Name => _fi.Name;
        public DirectoryInfo Directory => _fi.Directory;

        public HashValue? Hash
        {
            get => _hashValue;
            set
            {
                if (_hashValue.HasValue)
                    throw new InvalidOperationException("CAN ONLY BE SET ONCE");

                _hashValue = value;
            }
        }
        private HashValue? _hashValue;

        public void Delete()
        {
            _fi.Delete();
        }
    }

    internal class PointerFile : AriusArchiveItem
    {
        public const string Extension = ".pointer.arius";

        public PointerFile(FileInfo fi) : base(fi) { }
    }

    internal class BinaryFile : AriusArchiveItem
    {
        public BinaryFile(FileInfo fi) : base(fi) { }

        public IEnumerable<ChunkFile2> Chunks { get; set; }
    }

    internal class ChunkFile2 : AriusArchiveItem
    {
        public ChunkFile2(FileInfo fi) : base(fi) { }

        public EncryptedChunkFile2 EncryptedChunkFile { get; set; }
    }

    internal class EncryptedChunkFile2 : AriusArchiveItem
    {
        public const string Extension = ".7z.arius";

        public EncryptedChunkFile2(FileInfo fi) : base(fi) { }
    }


    internal class IndexDirectoryStep : ProcessStep<DirectoryInfo, AriusArchiveItem>
    {
        public override IEnumerable<AriusArchiveItem> Work(DirectoryInfo di)
        {
            foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories).AsParallel())
            {
                if (fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase))
                {
                    Console.WriteLine("PointerFile " + fi.Name);

                    yield return new PointerFile(fi);
                }
                else
                {
                    Console.WriteLine("BinaryFile " + fi.Name);

                    yield return new BinaryFile(fi);
                }
            }
        }
    }



    internal class AddHashStep : ProcessStep<AriusArchiveItem, AriusArchiveItem>
    {
        private readonly IHashValueProvider _hvp;

        public AddHashStep(IHashValueProvider hvp)
        {
            _hvp = hvp;
        }

        public override IEnumerable<AriusArchiveItem> Work(AriusArchiveItem workItem)
        {
            yield return AddHash((dynamic) workItem);
        }

        private AriusArchiveItem AddHash(PointerFile f)
        {
            Console.WriteLine("Hashing PointerFile " + f.Name);

            var h = File.ReadAllText(f.FileFullName);
            f.Hash = new HashValue {Value = h};

            Console.WriteLine("Hashing PointerFile " + f.Name + " done");

            return f;
        }

        private AriusArchiveItem AddHash(BinaryFile f)
        {
            Console.WriteLine("Hashing BinaryFile " + f.Name);
            
            var h = ((SHA256Hasher)_hvp).GetHashValue(f); //TODO remove cast)
            f.Hash = h;
            
            Console.WriteLine("Hashing BinaryFile " + f.Name + " done");

            return f;
        }
    }

    internal class AddChunksStep : ProcessStep<BinaryFile, BinaryFile>
    {
        private readonly IChunker _c;

        public AddChunksStep(IChunker c)
        {
            _c = c;
        }

        public override IEnumerable<BinaryFile> Work(BinaryFile f)
        {
            Console.WriteLine("Chunking BinaryFile " + f.Name);

            var cs = ((Chunker)_c).Chunk(f);
            f.Chunks = cs;

            Console.WriteLine("Chunking BinaryFile " + f.Name + " done");

            yield return f;
        }
    }

    internal class EncryptChunksStep : ProcessStep<ChunkFile2, ChunkFile2>
    {
        private readonly IEncrypter _e;

        public EncryptChunksStep(IEncrypter e)
        {
            _e = e;
        }

        public override IEnumerable<ChunkFile2> Work(ChunkFile2 f)
        {
            Console.WriteLine("Encrypting ChunkFile2 " + f.Name);

            var ecf = ((SevenZipCommandlineEncrypter)_e).Encrypt(f);
            f.EncryptedChunkFile = ecf;

            Console.WriteLine("Encrypting ChunkFile2 " + f.Name + " done");

            yield return f;
        }
    }

    internal class UploadChunkStep : ProcessStep<ChunkFile2, ChunkFile2>
    {
        public override IEnumerable<ChunkFile2> Work(ChunkFile2 workItem)
        {
            throw new NotImplementedException();
        }
    }

    internal class CreateManifestDbStep : ProcessStep<ChunkFile2, ChunkFile2>
    {
        public override IEnumerable<ChunkFile2> Work(ChunkFile2 workItem)
        {
            throw new NotImplementedException();
        }
    }

    internal class PointerCreatorStep : ProcessStep<BinaryFile, BinaryFile>
    {
        public override IEnumerable<BinaryFile> Work(BinaryFile workItem)
        {
            throw new NotImplementedException();
        }
    }

    internal class UpdateManifestDbStep : ProcessStep<PointerFile, PointerFile>
    {
        public override IEnumerable<PointerFile> Work(PointerFile workItem)
        {
            throw new NotImplementedException();
        }
    }


    public class ArchiveCommandExecutor2
    {
        public ArchiveCommandExecutor2(DirectoryInfo root)
        {
            _root = root;
        }

        private readonly DirectoryInfo _root;

        internal void Execute(IHashValueProvider h, IChunker c, IEncrypter e)
        {

            //Create pipeline steps
            var indexDirectoryStep = new IndexDirectoryStep();
            var addHashStep = new AddHashStep(h);
            var addChunksStep = new AddChunksStep(c);
            var encryptChunksStep = new EncryptChunksStep(e);
            var uploadChunkStep = new UploadChunkStep();
            var createManifestDbStep = new CreateManifestDbStep();
            var pointerCreatorStep = new PointerCreatorStep();
            var updateManifestDbStep = new UpdateManifestDbStep();


            var processedOrProcessingBinaries = new List<HashValue>();
            


            //Set up pipeline
            indexDirectoryStep.NextAction = item =>
            {
                addHashStep.Enqueue(item);
            };
            addHashStep.NextAction = item =>
            {
                if (item is BinaryFile binaryFileItem)
                {
                    lock (processedOrProcessingBinaries)
                    {
                        if (!processedOrProcessingBinaries.Contains(binaryFileItem.Hash!.Value))
                        { 
                            processedOrProcessingBinaries.Add(binaryFileItem.Hash!.Value); 
                            addChunksStep.Enqueue(binaryFileItem);
                        }
                        else
                        {
                            pointerCreatorStep.Enqueue(binaryFileItem);
                        }
                    }
                }
                else if (item is PointerFile pointerFileItem)
                {
                    updateManifestDbStep.Enqueue(pointerFileItem);
                }
                else
                {
                    throw new NotImplementedException();
                }
            };
            addChunksStep.NextAction = item =>
            {
                foreach (var i3 in item.Chunks)
                    encryptChunksStep.Enqueue(i3);
            };
            encryptChunksStep.NextAction = item =>
            {
                Console.WriteLine("encrypted");
            };
            // Upload

            // Create pointers

            //Start pipeline
            indexDirectoryStep.Enqueue(_root);


            Task.WaitAll(indexDirectoryStep.WorkerTask);

            



            //var toStart = new ConcurrentQueue<Task>();

            //var indexer = new Indexer();
            //toStart.Enqueue(indexer.GetTask(_root));

            //var hasher = new Hasher(h);
            //indexer.NewTask += (sender, eventArgs) =>
            //{
            //    toStart.Enqueue(hasher.GetTask(eventArgs.GetT));
            //};

            //var running = new ConcurrentDictionary<int, Task>();

            //int MAX_CONCURRENT = 5;

            //while (running.Any(t => !t.Value.IsCompleted) || !toStart.IsEmpty)
            //{
            //    // Remove completed tasks
            //    running.Where(t => t.Value.IsCompleted).ToList().ForEach(t => running.TryRemove(t.Key, out _));

            //    // Select next task to start
            //    toStart.TryDequeue(out var task);
            //    if (task == null || running.Count >= MAX_CONCURRENT)
            //    {
            //        Task.Delay(1000).Wait();
            //        continue;
            //    }

            //    running.AddOrUpdate(task.Id, task, (a, b) => throw new NotImplementedException());

            //    Task.Run(() =>
            //    {
            //        task.Start();
            //    });
            //}

            ////Task.WaitAll(running.ToArray()); //.Any(t => !t.IsCompleted))

            ////var hasherTask = new Task(() => hasher.GetWorkerTask());


            ////indexer.WorkerQueue.Enqueue(_root);

            //////Start backwards
            ////hasherTask.Start();
            ////indexerTask.Start();

            //////Wait until all workers are finished
            ////Task.WaitAll(indexerTask, hasherTask);
        }
    }
}
