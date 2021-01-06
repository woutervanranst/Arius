using Arius.Services;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius
{
    // https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/implementing-the-event-based-asynchronous-pattern

    abstract class Worker<TIn, TOut>
    {
        public abstract Task GetTask(TIn t);
        //{
        //    return Task.Run(Work);
        //}

        //public abstract void Work();

        public event WorkerEventHandler<TOut> NewTask;

        protected void OnWorker(TOut t)
        {
            NewTask?.Invoke(this, new WorkerEventArgs<TOut>(t));
        }
    }

    delegate void WorkerEventHandler<T>(object sender, WorkerEventArgs<T> e);

    class WorkerEventArgs<T> : EventArgs
    {
        public WorkerEventArgs(T t)
        {
            _t = t;
        }

        private readonly T _t;

        public T GetT => _t;
    }

    class Indexer : Worker<DirectoryInfo, AriusFile>
    {
        //public Indexer(DirectoryInfo di)
        //{
        //    _di = di;
        //}

        private readonly DirectoryInfo _di;

        public override Task GetTask(DirectoryInfo di)
        {
            var t = new Task(() =>
            {
                Console.WriteLine($"Indexing {di.FullName}");

                foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                {
                    var af = new AriusFile(fi);

                    OnWorker(af);
                }

                Console.WriteLine($"Indexing {di.FullName} done");
            });

            return t;
        }
    }

    class Hasher : Worker<AriusFile, AriusFile>
    {
        public Hasher(IHashValueProvider h)
        {
            _h = h;
        }

        readonly IHashValueProvider _h;

        public override Task GetTask(AriusFile af)
        {
            var t = new Task(() =>
            {
                Console.WriteLine($"Hashing {af.FileFullName}");
                af.HashValue = ((SHA256Hasher)_h).GetHashValue(af);
                Console.WriteLine($"Hashing {af.FileFullName}...Done");
            });

            return t;

            //var ll = new List<Task>();

            //do
            //{
            //    if (WorkerQueue.IsEmpty)
            //        Task.Delay(1000);
            //    else
            //    {
            //        WorkerQueue.TryDequeue(out var af);


            //        var x = Environment.ProcessorCount;
            //        //System.Threading.

            //        //var t = new Task(() => Work(af));
            //        //ll.Add(t);
            //        //t.Start();

            //        //TaskScheduler.Current.;

            //        //Parallel.;

            //        //ParallelOptions
            //    }
            //} while (!_previousWorkerCompleted() || !WorkerQueue.IsEmpty);
        }
    }

    class AriusFile
    {
        public AriusFile(FileInfo fi)
        {
            _fi = fi;


            // 1. Pointer

            // 2. LocalFile
        }
        public AriusFile(BlobItem bi)
        {
            // 3. BlobItem
        }

        private readonly FileInfo _fi;

        public HashValue? HashValue { get; set-; }
        public string FileFullName => _fi.FullName;
    }

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

        internal void Execute(IHashValueProvider h)
        {
            var toStart = new ConcurrentQueue<Task>();

            var indexer = new Indexer();
            toStart.Enqueue(indexer.GetTask(_root));

            var hasher = new Hasher(h);
            indexer.NewTask += (sender, eventArgs) =>
            {
                toStart.Enqueue(hasher.GetTask(eventArgs.GetT));
            };

            var running = new ConcurrentDictionary<int, Task>();

            int MAX_CONCURRENT = 5;

            while (running.Any(t => !t.Value.IsCompleted) || !toStart.IsEmpty)
            {
                // Remove completed tasks
                running.Where(t => t.Value.IsCompleted).ToList().ForEach(t => running.TryRemove(t.Key, out _));

                // Select next task to start
                toStart.TryDequeue(out var task);
                if (task == null || running.Count >= MAX_CONCURRENT)
                {
                    Task.Delay(1000).Wait();
                    continue;
                }

                running.AddOrUpdate(task.Id, task, (a, b) => throw new NotImplementedException());

                Task.Run(() =>
                {
                    task.Start();
                });
            }

            //Task.WaitAll(running.ToArray()); //.Any(t => !t.IsCompleted))

            //var hasherTask = new Task(() => hasher.GetWorkerTask());


            //indexer.WorkerQueue.Enqueue(_root);

            ////Start backwards
            //hasherTask.Start();
            //indexerTask.Start();

            ////Wait until all workers are finished
            //Task.WaitAll(indexerTask, hasherTask);
        }
    }
}
