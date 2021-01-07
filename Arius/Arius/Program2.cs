using Arius.Services;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Models;
using Azure.Storage.Blobs;
using Arius.CommandLine;
using Arius.Repositories;

namespace Arius
{
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




    internal class ArchiveCommandExecutor2
    {
        public ArchiveCommandExecutor2(DirectoryInfo root, IHashValueProvider hvp, IChunker c, IEncrypter e, IBlobCopier b)
        {
            _root = root;
            _hvp = hvp;
            _chunker = c;
            _encrypter = e;
            _blobCopier = b;

            _azureRepository = new AzureRepository() = new AzureRepository()

        }

        private readonly DirectoryInfo _root;
        private readonly IHashValueProvider _hvp;
        private readonly IChunker _chunker;
        private readonly IEncrypter _encrypter;
        private readonly IBlobCopier _blobCopier;

        private readonly AzureRepository _azureRepository;


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

            var getChunksBlock = new TransformManyBlock<BinaryFile, ChunkFile2>(binaryFile =>
                {
                    var addChunks = false;

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
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 4}
            );

            var encryptChunksBlock = new TransformBlock<ChunkFile2, EncryptedChunkFile2>(
                chunkFile => Encrypt(chunkFile),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 4});



            var uploadEncryptedChunksBlock = new TransformBlock<EncryptedChunkFile2, RemoteEncryptedChunkBlobItem2>(
                encryptedChunkFile =>  );

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
                new DataflowLinkOptions {PropagateCompletion = true},
                x => x is PointerFile);


            castToBinaryBlock.LinkTo(
                getChunksBlock,
                new DataflowLinkOptions { PropagateCompletion = true });

            getChunksBlock.LinkTo(
                encryptChunksBlock,
                new DataflowLinkOptions {PropagateCompletion = true});




            indexDirectoryBlock.Post(_root);
            indexDirectoryBlock.Complete();

            endBlock.Completion.Wait();
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

        private EncryptedChunkFile2 Encrypt(ChunkFile2 f)
        {
            Console.WriteLine("Encrypting ChunkFile2 " + f.Name);

            var ecf = ((SevenZipCommandlineEncrypter)_encrypter).Encrypt(f);

            Console.WriteLine("Encrypting ChunkFile2 " + f.Name + " done");

            return ecf;
        }

        
    }

    internal class AzureRepository
    {
        private readonly IBlobCopier _blobCopier;

        public AzureRepository(ICommandExecutorOptions options, IBlobCopier b)
        {
            _blobCopier = b;

            var o = (IRemoteChunkRepositoryOptions)options;

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";
            var bsc = new BlobServiceClient(connectionString);
            _bcc = bsc.GetBlobContainerClient(o.Container);
        }

        private readonly BlobContainerClient _bcc;

        private const string EncryptedChunkDirectoryName = "chunks";


        public IEnumerable<RemoteEncryptedChunkBlobItem2> GetAllChunkBlobItems()
        {
            return _bcc.GetBlobs(prefix: EncryptedChunkDirectoryName)
                .Select(bi => new RemoteEncryptedChunkBlobItem2(bi));
        }

        public RemoteEncryptedChunkBlobItem2 GetByName(string name, string folder = EncryptedChunkDirectoryName)
        {
            var bi = _bcc
                .GetBlobs(prefix: $"{folder}/{name}", traits: BlobTraits.Metadata & BlobTraits.CopyStatus)
                .Single();

            return new RemoteEncryptedChunkBlobItem2(bi);
        }

        private RemoteEncryptedChunkBlobItem2 Upload(EncryptedChunkFile2 ecf, AccessTier tier)
        {
            ((AzCopier)_blobCopier).Upload(ecf, tier,EncryptedChunkDirectoryName, false);

            return GetByName(ecf.Name);
        }
    }

    class RemoteEncryptedChunkBlobItem2
    {
        public RemoteEncryptedChunkBlobItem2(BlobItem bi)
        {

        }
    }
}
