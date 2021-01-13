using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.CommandLine
{
    class IndexDirectoryBlockProvider
    {
        public TransformManyBlock<DirectoryInfo, IFile> GetBlock()
        {
            return new(
                di => IndexDirectory(di),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 1}
            );
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
    }

    class AddHashBlockProvider
    {
        private readonly IHashValueProvider _hvp;
        private readonly bool _fastHash;

        public AddHashBlockProvider(IHashValueProvider hvp, bool fastHash)
        {
            _hvp = hvp;
            _fastHash = fastHash;
        }

        public TransformBlock<IFile, IFileWithHash> GetBlock()
        {
            return new(
                file => (IFileWithHash) AddHash((dynamic) file, _fastHash),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded}
            );
        }

        private IFileWithHash AddHash(PointerFile f, bool _)
        {
            Console.WriteLine("Hashing PointerFile " + f.Name);

            f.Hash = _hvp.GetHashValue(f); //) ReadHashFromPointerFile(f.FileFullName);

            Console.WriteLine("Hashing PointerFile " + f.Name + " done");

            return f;
        }

        private IFileWithHash AddHash(BinaryFile f, bool fastHash)
        {
            Console.WriteLine("Hashing BinaryFile " + f.Name);

            var h = default(HashValue?);

            if (fastHash)
            {
                var pointerFileInfo = new FileInfo(f.GetPointerFileFullName());
                if (pointerFileInfo.Exists)
                    h = _hvp.GetHashValue(new PointerFile(pointerFileInfo));
            }

            if (!h.HasValue)
                h = _hvp.GetHashValue(f); //TODO remove cast)

            f.Hash = h.Value;

            Console.WriteLine("Hashing BinaryFile " + f.Name + " done");

            return f;
        }
    }

    class AddRemoteManifestBlockProvider
    {
        private readonly List<HashValue> _uploadedManifestHashes;

        public AddRemoteManifestBlockProvider(List<HashValue> uploadedManifestHashes)
        {
            _uploadedManifestHashes = uploadedManifestHashes;
        }

        public TransformBlock<IFileWithHash, BinaryFile> GetBlock()
        {
            return new(
                item => AddRemoteManifest(item, _uploadedManifestHashes));
        }

        private BinaryFile AddRemoteManifest(IFileWithHash item, List<HashValue> uploadedManifestHashes)
        {
            var binaryFile = (BinaryFile) item;

            // Check whether the binaryFile isn't already uploaded or in the course of being uploaded
            lock (uploadedManifestHashes)
            {
                var h = binaryFile.Hash;
                if (uploadedManifestHashes.Contains(h))
                {
                    //Chunks & Manifest are already present - set the ManifestHash
                    binaryFile.ManifestHash = h;
                }
            }

            return binaryFile;
        }
    }

    class GetChunksForUploadBlockProvider
    {
        private readonly IChunker _chunker;
        private readonly Dictionary<HashValue, KeyValuePair<BinaryFile, List<HashValue>>> _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;
        private readonly List<HashValue> _uploadedOrUploadingChunks;

        public GetChunksForUploadBlockProvider(IChunker chunker,
            Dictionary<HashValue, KeyValuePair<BinaryFile, List<HashValue>>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated,
            AzureRepository azureRepository)
        {
            _chunker = chunker;
            _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;

            _uploadedOrUploadingChunks = azureRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).ToList();

        }

        public TransformManyBlock<BinaryFile, IChunkFile> GetBlock()
        {
            return new(
                binaryFile => GetChunksForUpload(binaryFile, _uploadedOrUploadingChunks, _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded});
        }

        private IEnumerable<IChunkFile> GetChunksForUpload(BinaryFile binaryFile, List<HashValue> uploadedOrUploadingChunks, Dictionary<HashValue, KeyValuePair<BinaryFile, List<HashValue>>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
        {
            var chunks = AddChunks(binaryFile);

            chunks = chunks.Select(chunk =>
            {
                lock (uploadedOrUploadingChunks)
                {
                    if (uploadedOrUploadingChunks.Contains(chunk.Hash))
                    {
                        chunk.Delete();
                        chunk.Uploaded = true;
                    }
                    else
                    {
                        lock (chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                        {
                            if (!chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.ContainsKey(binaryFile.Hash))
                                chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.Add(binaryFile.Hash,
                                    new KeyValuePair<BinaryFile, List<HashValue>>(binaryFile, new List<HashValue>() {chunk.Hash}));
                            else
                                chunksThatNeedToBeUploadedBeforeManifestCanBeCreated[binaryFile.Hash].Value.Add(chunk.Hash);
                        }

                        chunk.Uploaded = false; //ie to upload
                    }

                    uploadedOrUploadingChunks.Add(chunk.Hash);
                }

                return chunk;
            });

            return chunks;
        }

        private IEnumerable<IChunkFile> AddChunks(BinaryFile f)
        {
            Console.WriteLine("Chunking BinaryFile " + f.Name);

            var cs = _chunker.Chunk(f).ToArray();
            f.Chunks = cs;

            Console.WriteLine("Chunking BinaryFile " + f.Name + " done");

            return cs;
        }
    }

    class EncryptChunksBlockProvider
    {
        private readonly IConfiguration _config;
        private readonly IEncrypter _encrypter;

        public EncryptChunksBlockProvider(IConfiguration config, IEncrypter encrypter)
        {
            _config = config;
            _encrypter = encrypter;
        }

        public TransformBlock<IChunkFile, EncryptedChunkFile> GetBlock()
        {
            return new(
                chunkFile => EncryptChunks(chunkFile),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 8});
        }

        private EncryptedChunkFile EncryptChunks(IChunkFile f)
        {
            Console.WriteLine("Encrypting ChunkFile2 " + f.Name);

            var targetFile = new FileInfo(Path.Combine(_config.TempDir.FullName, "encryptedchunks", $"{f.Hash}{EncryptedChunkFile.Extension}"));

            _encrypter.Encrypt(f, targetFile, SevenZipCommandlineEncrypter.Compression.NoCompression, f is not BinaryFile);

            var ecf = new EncryptedChunkFile(targetFile, f.Hash);

            Console.WriteLine("Encrypting ChunkFile2 " + f.Name + " done");

            return ecf;
        }
    }

    class EnqueueEncryptedChunksForUploadBlockProvider
    {
        private readonly BlockingCollection<EncryptedChunkFile> _uploadQueue;

        public EnqueueEncryptedChunksForUploadBlockProvider(BlockingCollection<EncryptedChunkFile> uploadQueue)
        {
            _uploadQueue = uploadQueue;
        }

        public ActionBlock<EncryptedChunkFile> GetBlock()
        {
            return new(item => _uploadQueue.Add(item));
        }
    }

    class UploadTaskProvider
    {
        private readonly BlockingCollection<EncryptedChunkFile> _uploadQueue;
        private readonly ITargetBlock<EncryptedChunkFile[]> _uploadEncryptedChunksBlock;
        private readonly ActionBlock<EncryptedChunkFile> _enqueueEncryptedChunksForUploadBlock;

        const int AzCopyBatchSize = 256 * 1024 * 1024; //256 MB
        const int AzCopyBatchCount = 128;

        public UploadTaskProvider(BlockingCollection<EncryptedChunkFile> uploadQueue,
            ITargetBlock<EncryptedChunkFile[]> uploadEncryptedChunksBlock,
            ActionBlock<EncryptedChunkFile> enqueueEncryptedChunksForUploadBlock)
        {
            _uploadQueue = uploadQueue;
            _uploadEncryptedChunksBlock = uploadEncryptedChunksBlock;
            _enqueueEncryptedChunksForUploadBlock = enqueueEncryptedChunksForUploadBlock;
        }

        public Task GetTask()
        {
            return Task.Run(() =>
            {
                Thread.CurrentThread.Name = "Uploader";

                while (!_enqueueEncryptedChunksForUploadBlock.Completion.IsCompleted ||
                       //encryptChunksBlock.OutputCount > 0 || 
                       _uploadQueue.Count > 0)
                {
                    var uploadBatch = new List<EncryptedChunkFile>();
                    long size = 0;
                    foreach (var ecf in _uploadQueue.GetConsumingEnumerable())
                    {
                        uploadBatch.Add(ecf);
                        size += ecf.Length;
                    }

                    if (size >= AzCopyBatchSize ||
                        uploadBatch.Count >= AzCopyBatchCount ||
                        _uploadQueue.IsCompleted) //if we re at the end of the queue, upload the remainder
                    {
                        _uploadEncryptedChunksBlock.Post(uploadBatch.ToArray());
                        break;
                    }
                }
            });
        }
    }

    class UploadEncryptedChunksBlockProvider
    {
        private readonly ArchiveOptions _options;
        private readonly AzureRepository _azureRepository;

        public UploadEncryptedChunksBlockProvider(ArchiveOptions options, AzureRepository azureRepository)
        {
            _options = options;
            _azureRepository = azureRepository;
        }

        public TransformManyBlock<EncryptedChunkFile[], HashValue> GetBlock()
        {
            return new(
                encryptedChunkFiles =>
                {
                    //Upload the files
                    var uploadedBlobs = _azureRepository.Upload(encryptedChunkFiles, _options.Tier);

                    //Delete the files
                    foreach (var encryptedChunkFile in encryptedChunkFiles)
                        encryptedChunkFile.Delete();

                    return uploadedBlobs.Select(recbi => recbi.Hash);
                },
                new ExecutionDataflowBlockOptions() {MaxDegreeOfParallelism = 2});
        }
    }

    class ReconcileChunksWithManifestsBlockProvider
    {
        private readonly Dictionary<HashValue, KeyValuePair<BinaryFile, List<HashValue>>> _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;

        public ReconcileChunksWithManifestsBlockProvider(
            Dictionary<HashValue, KeyValuePair<BinaryFile, List<HashValue>>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
        {
            _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;
        }

        public TransformManyBlock<HashValue, BinaryFile> GetBlock()
        {
            return new( // IN: HashValue of Chunk , OUT: BinaryFiles for which to create Manifest
                hashOfUploadedChunk =>
                {
                    var manifestsToCreate = new List<BinaryFile>();

                    lock (_chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                    {
                        foreach (var kvp in _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated) //Key = HashValue van de Manifest, List = HashValue van de Chunks
                        {
                            // Remove the incoming ChunkHash from the list of prerequired
                            kvp.Value.Value.Remove(hashOfUploadedChunk);

                            // If the list of prereqs is empty
                            if (!kvp.Value.Value.Any())
                            {
                                // Add it to the list of manifests to be created
                                kvp.Value.Key.ManifestHash = kvp.Key;
                                manifestsToCreate.Add(kvp.Value.Key);
                            }
                        }

                        // Remove all reconciled manifests from the waitlist
                        foreach (var manifestHash in manifestsToCreate)
                            _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.Remove(manifestHash.Hash);
                    }

                    return manifestsToCreate;
                });
        }
    }

    class CreateManifestBlockProvider
    {
        public CreateManifestBlockProvider()
        {
        }

        public TransformBlock<BinaryFile, BinaryFile> GetBlock()
        {
            return new(binaryFile =>
            {
                var me = ManifestService.AddManifest(binaryFile);

                return binaryFile;
            });
        }
    }

    class ReconcileBinaryFilesWithManifestBlockProvider
    {
        private readonly List<HashValue> _uploadedManifestHashes;
        private readonly Dictionary<HashValue, List<BinaryFile>> _binaryFilesPerManifetHash;

        public ReconcileBinaryFilesWithManifestBlockProvider(List<HashValue> uploadedManifestHashes)
        {
            _uploadedManifestHashes = uploadedManifestHashes;
            _binaryFilesPerManifetHash = new Dictionary<HashValue, List<BinaryFile>>(); //Key = HashValue van de Manifest
        }

        public TransformManyBlock<BinaryFile, BinaryFile> GetBlock()
        {
            return new(binaryFile =>
            {
                lock (_binaryFilesPerManifetHash)
                {
                    //Add to the list an wait until EXACTLY ONE binaryFile with the 
                    if (!_binaryFilesPerManifetHash.ContainsKey(binaryFile.Hash))
                        _binaryFilesPerManifetHash.Add(binaryFile.Hash, new List<BinaryFile>());

                    // Add this binaryFile to the list of pointers to be created, once this manifest is created
                    _binaryFilesPerManifetHash[binaryFile.Hash].Add(binaryFile);

                    if (binaryFile.ManifestHash.HasValue)
                    {
                        lock (_uploadedManifestHashes)
                        {
                            _uploadedManifestHashes.Add(binaryFile.ManifestHash.Value);
                        }
                    }

                    if (_uploadedManifestHashes.Contains(binaryFile.Hash))
                    {
                        var pointersToCreate = _binaryFilesPerManifetHash[binaryFile.Hash].ToArray();
                        _binaryFilesPerManifetHash[binaryFile.Hash].Clear();

                        return pointersToCreate;
                    }
                    else
                        return Enumerable.Empty<BinaryFile>(); // NOTHING TO PASS ON TO THE NEXT STAGE
                }

                /* Input is either
                    If the Manifest already existed remotely, the BinaryFile with Hash and ManifestHash, witout Chunks
                    If the Manifest did not already exist, it will be uploaded by now - wit Hash and ManifestHash
                    If the Manifest did not already exist, and the file is a duplicate, with Hash but NO ManifestHash
                    The manifest did initially not exist, but was uploaded in the mean time
                 */
            });
        }
    }

    class CreatePointerBlockProvider
    {
        public CreatePointerBlockProvider()
        {
        }

        public TransformBlock<BinaryFile, PointerFile> GetBlock()
        {
            return new(binaryFile =>
            {
                var p = binaryFile.EnsurePointerExists();

                return p;
            });
        }
    }

    class UpdateManifestBlockProvider
    {
        private readonly ILogger _logger;
        private readonly DateTime _version;
        private readonly DirectoryInfo _root;

        public UpdateManifestBlockProvider(ILogger logger, DateTime version, DirectoryInfo root)
        {
            _logger = logger;
            _version = version;
            _root = root;
        }

        public ActionBlock<PointerFile> GetBlock()
        {
            return new(pointerFile =>
            {
                // Update the manifest
                using (var db = new ManifestStore())
                {
                    var me = db.Manifests
                        .Include(me => me.Entries)
                        .Single(m => m.HashValue == pointerFile.Hash!.Value);

                    //TODO iets met PointerFileEntryEqualityComparer?

                    var e = new PointerFileEntry
                    {
                        RelativeName = Path.GetRelativePath(_root.FullName, pointerFile.FullName),
                        Version = _version,
                        CreationTimeUtc = File.GetCreationTimeUtc(pointerFile.FullName), //TODO
                        LastWriteTimeUtc = File.GetLastWriteTimeUtc(pointerFile.FullName),
                        IsDeleted = false
                    };

                    var pfeec = new PointerFileEntryEqualityComparer();
                    if (!me.Entries.Contains(e, pfeec))
                        me.Entries.Add(e);

                    _logger.LogInformation($"Added {e.RelativeName}");

                    db.SaveChanges();
                }
            });
        }
    }

    class RemoveDeletedPointersTaskProvider
    {
        private readonly ILogger _logger;
        private readonly DateTime _version;
        private readonly DirectoryInfo _root;

        public RemoveDeletedPointersTaskProvider(ILogger logger, DateTime version, DirectoryInfo root)
        {
            _logger = logger;
            _version = version;
            _root = root;
        }

        public Task GetTask()
        {
            return Task.Run(() =>
            {
                using var db = new ManifestStore();

                //Not parallel foreach since DbContext is not thread safe
                foreach (var m in db.Manifests.Include(m => m.Entries))
                {
                    foreach (var e in m.GetLastEntries(false).Where(e => e.Version != _version))
                    {
                        //TODO iets met PointerFileEntryEqualityComparer?

                        var p = Path.Combine(_root.FullName, e.RelativeName);
                        if (!File.Exists(p))
                        {
                            m.Entries.Add(new PointerFileEntry()
                            {
                                RelativeName = e.RelativeName,
                                Version = _version,
                                IsDeleted = true,
                                CreationTimeUtc = null,
                                LastWriteTimeUtc = null
                            });

                            _logger.LogInformation($"Marked {e.RelativeName} as deleted");
                        }
                    }
                }

                db.SaveChanges();
            });
            
        }


    }
}
