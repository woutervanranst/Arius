using Arius.Services;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections;
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
using System.Threading.Tasks.Dataflow;
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


    //internal class IndexDirectoryStep : ProcessStep<DirectoryInfo, AriusArchiveItem>
    //{
    //    public override IEnumerable<AriusArchiveItem> Work(DirectoryInfo di)
    //    {
    //        foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories).AsParallel())
    //        {
    //            if (fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase))
    //            {
    //                Console.WriteLine("PointerFile " + fi.Name);

    //                yield return new PointerFile(fi);
    //            }
    //            else
    //            {
    //                Console.WriteLine("BinaryFile " + fi.Name);

    //                yield return new BinaryFile(fi);
    //            }
    //        }
    //    }
    //}



    //internal class AddHashStep : ProcessStep<AriusArchiveItem, AriusArchiveItem>
    //{
    //    private readonly IHashValueProvider _hvp;

    //    public AddHashStep(IHashValueProvider hvp)
    //    {
    //        _hvp = hvp;
    //    }

    //    public override IEnumerable<AriusArchiveItem> Work(AriusArchiveItem workItem)
    //    {
    //        yield return AddHash((dynamic) workItem);
    //    }

    //    private AriusArchiveItem AddHash(PointerFile f)
    //    {
    //        Console.WriteLine("Hashing PointerFile " + f.Name);

    //        var h = File.ReadAllText(f.FileFullName);
    //        f.Hash = new HashValue {Value = h};

    //        Console.WriteLine("Hashing PointerFile " + f.Name + " done");

    //        return f;
    //    }

    //    private AriusArchiveItem AddHash(BinaryFile f)
    //    {
    //        Console.WriteLine("Hashing BinaryFile " + f.Name);
            
    //        var h = ((SHA256Hasher)_hvp).GetHashValue(f); //TODO remove cast)
    //        f.Hash = h;
            
    //        Console.WriteLine("Hashing BinaryFile " + f.Name + " done");

    //        return f;
    //    }
    //}

    //internal class AddChunksStep : ProcessStep<BinaryFile, BinaryFile>
    //{
    //    private readonly IChunker _c;

    //    public AddChunksStep(IChunker c)
    //    {
    //        _c = c;
    //    }

    //    public override IEnumerable<BinaryFile> Work(BinaryFile f)
    //    {
    //        Console.WriteLine("Chunking BinaryFile " + f.Name);

    //        var cs = ((Chunker)_c).Chunk(f);
    //        f.Chunks = cs;

    //        Console.WriteLine("Chunking BinaryFile " + f.Name + " done");

    //        yield return f;
    //    }
    //}

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


    internal class ArchiveCommandExecutor2
    {
        public ArchiveCommandExecutor2(DirectoryInfo root, IHashValueProvider hvp, IChunker c, IEncrypter e)
        {
            _root = root;
            _hvp = hvp;
            _chunker = c;
            _encrypter = e;
        }

        private readonly DirectoryInfo _root;
        private readonly IHashValueProvider _hvp;
        private readonly IChunker _chunker;
        private readonly IEncrypter _encrypter;


        internal void Execute()
        {
            var indexDirectoryBlock = new TransformManyBlock<DirectoryInfo, AriusArchiveItem>(
                di => IndexDirectory(di),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 1}
                );

            var addHashBlock = new TransformBlock<AriusArchiveItem, AriusArchiveItem>(
                item => AddHash(item),
                //item => item,
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4}
            );

            var castToBinaryBlock = new TransformBlock<AriusArchiveItem, BinaryFile>(item => (BinaryFile) item);
            var castToPointerBlock = new TransformBlock<AriusArchiveItem, PointerFile>(item => (PointerFile) item);

            var processedOrProcessingBinaries = new List<HashValue>();

            var addChunksBlock = new TransformManyBlock<BinaryFile, ChunkFile2>(binaryFile =>
                {
                    bool addChunks = false;

                    lock (processedOrProcessingBinaries)
                    {
                        var h = binaryFile.Hash!.Value;
                        if (!processedOrProcessingBinaries.Contains(h))
                        {
                            processedOrProcessingBinaries.Add(h);
                            addChunks = true;
                        }
                        
                    }

                    if (addChunks)
                        return AddChunks(binaryFile);
                    else
                        return Enumerable.Empty<ChunkFile2>();
                },
                new ExecutionDataflowBlockOptions() {MaxDegreeOfParallelism = 4}
            );

            //var encryptChunksBlock = new TransformBlock<>()

            var pointerCreateBlock = new TransformBlock<AriusArchiveItem, AriusArchiveItem>(item => item);

            var endBlock = new ActionBlock<AriusArchiveItem>(item => Console.WriteLine("done"));


            indexDirectoryBlock.LinkTo(
                addHashBlock,
                new DataflowLinkOptions {PropagateCompletion = true});


            addHashBlock.LinkTo(
                castToBinaryBlock,
                new DataflowLinkOptions { PropagateCompletion = true },
                x => x is BinaryFile);

            addHashBlock.LinkTo(
                castToPointerBlock,
                new DataflowLinkOptions() {PropagateCompletion = true},
                x => x is PointerFile);


            castToBinaryBlock.LinkTo(
                addChunksBlock,
                new DataflowLinkOptions() { PropagateCompletion = true },
                x => true);


            //addHashBlock.LinkTo(
            //    addChunksBlock,
            //    new DataflowLinkOptions { PropagateCompletion = true },
            //    x => GetDocumentLanguage(x) == Language.Spanish); //5



            indexDirectoryBlock.Post(_root);
            indexDirectoryBlock.Complete();

            endBlock.Completion.Wait();


            //var processedOrProcessingBinaries = new List<HashValue>();



            ////Set up pipeline
            //addHashStep.NextAction = item =>
            //{
            //    if (item is BinaryFile binaryFileItem)
            //    {
            //        lock (processedOrProcessingBinaries)
            //        {
            //            if (!processedOrProcessingBinaries.Contains(binaryFileItem.Hash!.Value))
            //            { 
            //                processedOrProcessingBinaries.Add(binaryFileItem.Hash!.Value); 
            //                addChunksStep.Enqueue(binaryFileItem);
            //            }
            //            else
            //            {
            //                pointerCreatorStep.Enqueue(binaryFileItem);
            //            }
            //        }
            //    }
            //    else if (item is PointerFile pointerFileItem)
            //    {
            //        updateManifestDbStep.Enqueue(pointerFileItem);
            //    }
            //    else
            //    {
            //        throw new NotImplementedException();
            //    }
            //};
            //addChunksStep.NextAction = item =>
            //{
            //    foreach (var i3 in item.Chunks)
            //        encryptChunksStep.Enqueue(i3);
            //};
            //encryptChunksStep.NextAction = item =>
            //{
            //    Console.WriteLine("encrypted");
            //};
            //// Upload

            //// Create pointers

            ////Start pipeline
            //indexDirectoryStep.Enqueue(_root);
        }

        private IEnumerable<AriusArchiveItem> IndexDirectory(DirectoryInfo di)
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


        public AriusArchiveItem AddHash(AriusArchiveItem workItem)
        {
            return AddHash((dynamic)workItem);
        }

        private AriusArchiveItem AddHash(PointerFile f)
        {
            Console.WriteLine("Hashing PointerFile " + f.Name);

            var h = File.ReadAllText(f.FileFullName);
            f.Hash = new HashValue { Value = h };

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

        public IEnumerable<ChunkFile2> AddChunks(BinaryFile f)
        {
            Console.WriteLine("Chunking BinaryFile " + f.Name);

            var cs = ((Chunker)_chunker).Chunk(f);

            Console.WriteLine("Chunking BinaryFile " + f.Name + " done");

            return cs;
        }
    }
}
