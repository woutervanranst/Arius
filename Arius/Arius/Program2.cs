using Arius.Services;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly AutoResetEvent are = new(false);

        private Task workerTask;


        protected ProcessStep()
        {
            StartWork();
        }

        private readonly ConcurrentQueue<TIn> _queue = new();

        public void Enqueue(TIn item)
        {
            _queue.Enqueue(item);
            are.Set();
        }

        private void StartWork()
        {
            workerTask = Task.Run(async () =>
            {
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
    }


    internal class DirectoryIndexStep : ProcessStep<DirectoryInfo, AriusArchiveItem>
    {
        //public override async IAsyncEnumerable<AriusArchiveItem> Work(DirectoryInfo di)
        //{
        //    foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories).AsParallel())
        //    {
        //        if (fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase))
        //        {
        //            Console.WriteLine("PointerFile " + fi.Name);

        //            //await Task.Yield();

        //            yield return new PointerFile(fi);
        //        }
        //        else
        //        {
        //            Console.WriteLine("BinaryFile " + fi.Name);

        //            //await Task.Yield();

        //            yield return new BinaryFile(fi);
        //        }
        //    }
        //}




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



    internal class Hasher : ProcessStep<AriusArchiveItem, AriusArchiveItem>
    {
        private readonly IHashValueProvider _hvp;

        public Hasher(IHashValueProvider hvp)
        {
            _hvp = hvp;
        }

        public override IEnumerable<AriusArchiveItem> Work(AriusArchiveItem workItem)
        {
            yield return Work2((dynamic) workItem);
        }

        private AriusArchiveItem Work2(PointerFile f)
        {
            Console.WriteLine("Hashing PointerFile " + f.Name);

            var h = File.ReadAllText(f.FileFullName);
            f.Hash = new HashValue {Value = h};

            Console.WriteLine("Hashing PointerFile " + f.Name + " done");

            return f;
        }

        private AriusArchiveItem Work2(BinaryFile f)
        {
            Console.WriteLine("Hashing BinaryFile " + f.Name);
            
            var h = ((SHA256Hasher)_hvp).GetHashValue(f); //TODO remove cast)
            f.Hash = h;
            
            Console.WriteLine("Hashing BinaryFile " + f.Name + " done");

            return f;
        }
    }

    internal class Chunker2 : ProcessStep<AriusArchiveItem, AriusArchiveItem>
    {
        private readonly IChunker _c;

        public Chunker2(IChunker c)
        {
            _c = c;
        }

        public override IEnumerable<AriusArchiveItem> Work(AriusArchiveItem workItem)
        {
            yield return Work2((dynamic)workItem);
        }

        private AriusArchiveItem Work2(PointerFile f)
        {
            Console.WriteLine("Chunking PointerFile " + f.Name + " - nothing to do");

            return f;
        }

        private AriusArchiveItem Work2(BinaryFile f)
        {
            Console.WriteLine("Chunking BinaryFile " + f.Name);

            var cs = ((Arius.Services.Chunker)_c).Chunk(f);
            f.Chunks = cs;

            Console.WriteLine("Chunking BinaryFile " + f.Name + " done");

            return f;
        }
    }

    //internal class Hasher : Worker<AriusFile, AriusFile>
    //{
    //    public Hasher(IHashValueProvider h)
    //    {
    //        _h = h;
    //    }

    //    private readonly IHashValueProvider _h;

    //    public override Task GetTask(AriusFile af)
    //    {
    //        var t = new Task(() =>
    //        {
    //            Console.WriteLine($"Hashing {af.FileFullName}");
    //            af.HashValue = ((SHA256Hasher)_h).GetHashValue(af);
    //            Console.WriteLine($"Hashing {af.FileFullName}...Done");
    //        });

    //        return t;

    //        //var ll = new List<Task>();

    //        //do
    //        //{
    //        //    if (WorkerQueue.IsEmpty)
    //        //        Task.Delay(1000);
    //        //    else
    //        //    {
    //        //        WorkerQueue.TryDequeue(out var af);


    //        //        var x = Environment.ProcessorCount;
    //        //        //System.Threading.

    //        //        //var t = new Task(() => Work(af));
    //        //        //ll.Add(t);
    //        //        //t.Start();

    //        //        //TaskScheduler.Current.;

    //        //        //Parallel.;

    //        //        //ParallelOptions
    //        //    }
    //        //} while (!_previousWorkerCompleted() || !WorkerQueue.IsEmpty);
    //    }
    //}

    //internal class AriusFile
    //{
    //    public AriusFile(FileInfo fi)
    //    {
    //        _fi = fi;


    //        // 1. Pointer

    //        // 2. LocalFile
    //    }
    //    public AriusFile(BlobItem bi)
    //    {
    //        // 3. BlobItem
    //    }

    //    private readonly FileInfo _fi;

    //    public HashValue? HashValue { get; set-; }
    //    public string FileFullName => _fi.FullName;
    //}

    //class AriusFileBuilder
    //{
    //    public static AriusFileBuilder Init()
    //    {
    //        return new AriusFileBuilder();
    //    }

    //    public AriusFileBuilder FromFileInfo(FileInfo fi)
    //    {
            
    //    }

    //    //private AriusFileBuilder(AriusFile af)
    //    //{
    //    //    _af = af;
    //    //}
    //    private readonly AriusFile _af;


    //    public AriusFile CreateAriusFile(FileInfo fi)
    //    {
    //        return new AriusFile(fi);

    //        //if (fi.Name.EndsWith(".arius"))
    //        //{

    //        //}
    //        //else
    //        //{

    //        //}
    //    }

    //    public AriusFileBuilder EnsureHash()
    //}


    public class ArchiveCommandExecutor2
    {
        public ArchiveCommandExecutor2(DirectoryInfo root)
        {
            _root = root;
        }

        private readonly DirectoryInfo _root;

        internal void Execute(IHashValueProvider h, IChunker c)
        {

            //Create pipeline steps
            var indexer = new DirectoryIndexStep();
            var hasher = new Hasher(h);
            var chunker = new Chunker2(c);

            //Set up pipeline
            indexer.NextAction = item =>
            {
                hasher.Enqueue(item);
            };
            hasher.NextAction = item =>
            {
                chunker.Enqueue(item);
            };
            chunker.NextAction = item =>
            {
                Console.WriteLine("chunked");
            };

            //Start pipeline
            indexer.Enqueue(_root);


            Task.WaitAll(indexer.WorkerTask);

            



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
