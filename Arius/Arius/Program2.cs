using Arius.Services;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace Arius5
{
    // https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/implementing-the-event-based-asynchronous-pattern

    abstract class Worker<TWorker>
    {
        public Task StartWorkAsync()
        {
            return Task.Run(Work);
        }

        public abstract void Work();

        public event StartWorkCompletedEventHandler StartWorkCompleted;

        public event ProgressChangedEventHandler StartWorkProgressChanged;

        protected void OnStartWorkProgressChanged(int progressPercentage, object userState)
        {
            StartWorkProgressChanged?.Invoke(this, new ProgressChangedEventArgs(progressPercentage, userState));
        }

        private ConcurrentQueue<TWorker> workerQueue = new ConcurrentQueue<TWorker>();
        public ConcurrentQueue<TWorker> WorkerQueue => workerQueue; 
    }

    delegate void StartWorkCompletedEventHandler(object sender, StartWorkCompletedEventArgs e);

    class StartWorkCompletedEventArgs : AsyncCompletedEventArgs
    {
        public StartWorkCompletedEventArgs(Exception error, bool cancelled, object userState) : base(error, cancelled, userState)
        {
        }
    }

    class Indexer : Worker<DirectoryInfo>
    {
        public override void Work()
        {
            while (!WorkerQueue.IsEmpty)
            {
                WorkerQueue.TryDequeue(out var di);

                foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                {
                    var af = new AriusFile(fi);

                    OnStartWorkProgressChanged(0, af);
                }
            }
        }
    }

    class Hasher : Worker<AriusFile>
    {
        public Hasher(SHA256Hasher h,  Func<bool> previousWorkerCompleted)
        {
            _previousWorkerCompleted = previousWorkerCompleted;
            _h = h;
        }

        private readonly Func<bool> _previousWorkerCompleted;
        SHA256Hasher _h;

        public override void Work()
        {
            var ll = new List<Task>();

            do
            {
                if (WorkerQueue.IsEmpty)
                    Task.Delay(1000);
                else
                {
                    WorkerQueue.TryDequeue(out var af);


                    var x = Environment.ProcessorCount;
                    //System.Threading.

                    //var t = new Task(() => Work(af));
                    //ll.Add(t);
                    //t.Start();

                    //TaskScheduler.Current.;

                    //Parallel.;

                    //ParallelOptions
                }
            } while (!_previousWorkerCompleted() || !WorkerQueue.IsEmpty);
        }

        private void Work(AriusFile f)
        {
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

        public HashValue? HashValue { get; }
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


    public class ArchiveCommandExecutor
    {
        public ArchiveCommandExecutor(DirectoryInfo root)
        {
            _root = root;
        }

        private readonly DirectoryInfo _root;

        internal void Execute(SHA256Hasher h)
        {
            var indexer = new Indexer();
            var indexerTask = new Task(() => indexer.StartWorkAsync());

            var hasher = new Hasher(() => indexerTask.IsCompleted);
            indexer.StartWorkProgressChanged += (sender, eventArgs) =>
            {
                hasher.WorkerQueue.Enqueue((AriusFile)eventArgs.UserState);
            };
            var hasherTask = new Task(() => hasher.StartWorkAsync());


            indexer.WorkerQueue.Enqueue(_root);

            //Start backwards
            hasherTask.Start();
            indexerTask.Start();

            //Wait until all workers are finished
            Task.WaitAll(indexerTask, hasherTask);
        }
    }
}
