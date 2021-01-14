using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
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

        private IEnumerable<AriusArchiveItem> IndexDirectory(DirectoryInfo root)
        {
            foreach (var fi in root.GetFiles("*", SearchOption.AllDirectories).AsParallel())
            {
                if (fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase))
                {
                    Console.WriteLine("PointerFile " + fi.Name);

                    yield return new PointerFile(root, fi);
                }
                else
                {
                    Console.WriteLine("BinaryFile " + fi.Name);

                    yield return new BinaryFile(root, fi);
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
                var pointerFileInfo = new FileInfo(f.GetPointerFileFullName()); //TODO refactor into PointerServuce
                if (pointerFileInfo.Exists)
                    h = _hvp.GetHashValue(new PointerFile(f.Root, pointerFileInfo));
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
        private readonly Dictionary<BinaryFile, List<HashValue>> _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;
        private readonly List<HashValue> _uploadedOrUploadingChunks;

        public GetChunksForUploadBlockProvider(IChunker chunker,
            Dictionary<BinaryFile, List<HashValue>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated,
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

        private IEnumerable<IChunkFile> GetChunksForUpload(BinaryFile binaryFile, List<HashValue> uploadedOrUploadingChunks, Dictionary<BinaryFile, List<HashValue>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
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
                            if (!chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.ContainsKey(binaryFile))
                                chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.Add(binaryFile,
                                    new List<HashValue>(new [] {chunk.Hash}));
                            else
                                chunksThatNeedToBeUploadedBeforeManifestCanBeCreated[binaryFile].Add(chunk.Hash);
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

            var ecf = new EncryptedChunkFile(f.Root, targetFile, f.Hash);

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
        private readonly Dictionary<BinaryFile, List<HashValue>> _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;

        public ReconcileChunksWithManifestsBlockProvider(
            Dictionary<BinaryFile, List<HashValue>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
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
                            kvp.Value.Remove(hashOfUploadedChunk);

                            // If the list of prereqs is empty
                            if (!kvp.Value.Any())
                            {
                                // Add it to the list of manifests to be created
                                kvp.Key.ManifestHash = kvp.Key.Hash;
                                manifestsToCreate.Add(kvp.Key);
                            }
                        }

                        // Remove all reconciled manifests from the waitlist
                        foreach (var binaryFile in manifestsToCreate)
                            _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.Remove(binaryFile);
                    }

                    return manifestsToCreate;
                });
        }
    }

    class CreateManifestBlockProvider
    {
        private readonly AzureRepository _azureRepository;

        public CreateManifestBlockProvider(AzureRepository azureRepository)
        {
            _azureRepository = azureRepository;
        }

        public TransformBlock<BinaryFile, BinaryFile> GetBlock()
        {
            return new(async binaryFile =>
            {
                await _azureRepository.AddManifestAsync(binaryFile);

                return binaryFile;
            });
        }
    }

    class ReconcileBinaryFilesWithManifestBlockProvider
    {
        private readonly List<HashValue> _uploadedManifestHashes;
        private readonly Dictionary<HashValue, List<BinaryFile>> _binaryFilesPerManifestHash;

        public ReconcileBinaryFilesWithManifestBlockProvider(List<HashValue> uploadedManifestHashes)
        {
            _uploadedManifestHashes = uploadedManifestHashes;
            _binaryFilesPerManifestHash = new Dictionary<HashValue, List<BinaryFile>>(); //Key = HashValue van de Manifest
        }

        public TransformManyBlock<BinaryFile, BinaryFile> GetBlock()
        {
            return new(binaryFile =>
            {
                lock (_binaryFilesPerManifestHash)
                {
                    //Add to the list an wait until EXACTLY ONE binaryFile with the 
                    if (!_binaryFilesPerManifestHash.ContainsKey(binaryFile.Hash))
                        _binaryFilesPerManifestHash.Add(binaryFile.Hash, new List<BinaryFile>());

                    // Add this binaryFile to the list of pointers to be created, once this manifest is created
                    _binaryFilesPerManifestHash[binaryFile.Hash].Add(binaryFile);

                    if (binaryFile.ManifestHash.HasValue)
                    {
                        lock (_uploadedManifestHashes)
                        {
                            _uploadedManifestHashes.Add(binaryFile.ManifestHash.Value);
                        }
                    }

                    if (_uploadedManifestHashes.Contains(binaryFile.Hash))
                    {
                        var pointersToCreate = _binaryFilesPerManifestHash[binaryFile.Hash].ToArray();
                        _binaryFilesPerManifestHash[binaryFile.Hash].Clear();

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
                var p = binaryFile.CreatePointerFileIfNotExists();

                return p;
            });
        }
    }

    class CreatePointerFileEntryIfNotExistsBlockProvider
    {
        private readonly ILogger _logger;
        private readonly AzureRepository _azureRepository;
        private readonly DateTime _version;

        public CreatePointerFileEntryIfNotExistsBlockProvider(ILogger logger, AzureRepository azureRepository, DateTime version)
        {
            _logger = logger;
            _azureRepository = azureRepository;
            _version = version;
        }

        public ActionBlock<PointerFile> GetBlock()
        {
            return new(async pointerFile =>
            {
                await _azureRepository.CreatePointerFileEntryIfNotExistsAsync(pointerFile, _version);
            });
        }
    }

    class RemoveDeletedPointersTaskProvider
    {
        private readonly ILogger _logger;
        private readonly AzureRepository _azureRepository;
        private readonly DateTime _version;
        private readonly DirectoryInfo _root;

        public RemoveDeletedPointersTaskProvider(ILogger logger, AzureRepository azureRepository, DateTime version, DirectoryInfo root)
        {
            _logger = logger;
            _azureRepository = azureRepository;
            _version = version;
            _root = root;
        }

        public Task GetTask()
        {
            return new (() =>
            {
                var es = _azureRepository
                    .GetLastEntries(_version, true) //Get all entries until NOW and include the entries marked as deleted
                    .Where(e => e.Version < _version); // that were not created in the current run (those are assumed to be up to date)

                var xx = es.ToList(); //TODO DELETE

                Parallel.ForEach(es, async pfe =>
                {
                    //TODO iets met PointerFileEntryEqualityComparer?

                    var pointerFullName = Path.Combine(_root.FullName, pfe.RelativeName);
                    if (!File.Exists(pointerFullName) && !pfe.IsDeleted)
                        await _azureRepository.CreatePointerFileEntryIfNotExistsAsync(pfe, _version, true);
                });
            });
        }
    }

    class ExportToJsonTaskProvider
    {
        private readonly AzureRepository _azureRepository;
        //private readonly ILogger _logger;
        //private readonly DateTime _version;
        //private readonly DirectoryInfo _root;

        //public ExportToJsonTaskProvider(ILogger logger, DateTime version, DirectoryInfo root)
        //{
        //    _logger = logger;
        //    _version = version;
        //    _root = root;
        //}

        public ExportToJsonTaskProvider(AzureRepository azureRepository)
        {
            _azureRepository = azureRepository;
        }

        public Task GetTask()
        {
            return new(async () => 
            {

                //using (System.IO.Stream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                //using (GZipInputStream gzipStream = new GZipInputStream(fs))
                //using (StreamReader streamReader = new StreamReader(gzipStream))
                //using (JsonTextReader reader = new JsonTextReader(streamReader))
                //{
                //    reader.SupportMultipleContent = true;
                //    var serializer = new JsonSerializer();
                //    while (reader.Read())
                //    {
                //        if (reader.TokenType == JsonToken.StartObject)
                //        {
                //            var t = serializer.Deserialize<Element>(reader);
                //            //Add custom logic here - perhaps a yield return?
                //        }
                //    }
                //}

                using Stream file = File.Create(@"c:\ha.json");

                await JsonSerializer.SerializeAsync(file, _azureRepository.GetAllManifestEntriesWithChunksAndPointerFileEntries() ,
                    new JsonSerializerOptions {WriteIndented = true});
            });
        }
    }
}