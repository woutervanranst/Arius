using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
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
    internal class IndexDirectoryBlockProvider
    {
        private readonly ILogger<IndexDirectoryBlockProvider> _logger;
        private readonly DirectoryInfo _root;

        public IndexDirectoryBlockProvider(ILogger<IndexDirectoryBlockProvider> logger, ArchiveOptions options)
        {
            _logger = logger;
            _root = new DirectoryInfo(options.Path);
        }

        public TransformManyBlock<DirectoryInfo, IFile> GetBlock()
        {
            return new(
                di => IndexDirectory(_root),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 1}
            );
        }

        private IEnumerable<AriusArchiveItem> IndexDirectory(DirectoryInfo root)
        {
            foreach (var fi in root.GetFiles("*", SearchOption.AllDirectories).AsParallel())
            {
                if (fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase))
                {
                    _logger.LogInformation("PointerFile " + fi.Name);

                    yield return new PointerFile(root, fi);
                }
                else
                {
                    _logger.LogInformation("BinaryFile " + fi.Name);

                    yield return new BinaryFile(root, fi);
                }
            }
        }
    }

    internal class AddHashBlockProvider
    {
        private readonly ILogger<AddHashBlockProvider> _logger;
        private readonly IHashValueProvider _hvp;

        public AddHashBlockProvider(ILogger<AddHashBlockProvider> logger, IHashValueProvider hvp, ArchiveOptions options)
        {
            _logger = logger;
            _hvp = hvp;
        }

        public TransformBlock<IFile, IFileWithHash> GetBlock()
        {
            return new(
                file => (IFileWithHash) AddHash((dynamic) file),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded}
            );
        }

        private IFileWithHash AddHash(PointerFile f)
        {
            //_logger.LogInformation("Hashing PointerFile " + f.Name);

            //f.Hash = _hvp.GetHashValue(f); //) ReadHashFromPointerFile(f.FileFullName);

            //_logger.LogInformation("Hashing PointerFile " + f.Name + " done");

            return f;
        }

        private IFileWithHash AddHash(BinaryFile f)
        {
            _logger.LogInformation("Hashing BinaryFile " + f.Name);

            f.Hash = _hvp.GetHashValue(f);

            _logger.LogInformation("Hashing BinaryFile " + f.Name + " done");

            return f;
        }
    }

    internal class AddRemoteManifestBlockProvider
    {
        private List<HashValue> _uploadedManifestHashes;

        public AddRemoteManifestBlockProvider AddUploadedManifestHashes(List<HashValue> uploadedManifestHashes)
        {
            _uploadedManifestHashes = uploadedManifestHashes;

            return this;
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

    internal class GetChunksForUploadBlockProvider
    {
        private readonly IChunker _chunker;
        private readonly List<HashValue> _uploadedOrUploadingChunks;

        private Dictionary<BinaryFile, List<HashValue>> _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;


        public GetChunksForUploadBlockProvider(IChunker chunker,
            AzureRepository azureRepository)
        {
            _chunker = chunker;

            _uploadedOrUploadingChunks = azureRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).ToList();

        }

        public GetChunksForUploadBlockProvider SetChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(Dictionary<BinaryFile, List<HashValue>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
        {
            _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;

            return this;
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

    internal class EncryptChunksBlockProvider
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

            var targetFile = new FileInfo(Path.Combine(_config.UploadTempDir.FullName, "encryptedchunks", $"{f.Hash}{EncryptedChunkFile.Extension}"));

            _encrypter.Encrypt(f, targetFile, SevenZipCommandlineEncrypter.Compression.NoCompression, f is not BinaryFile);

            var ecf = new EncryptedChunkFile(f.Root, targetFile, f.Hash);

            Console.WriteLine("Encrypting ChunkFile2 " + f.Name + " done");

            return ecf;
        }
    }

    internal class EnqueueEncryptedChunksForUploadBlockProvider
    {
        private BlockingCollection<EncryptedChunkFile> _uploadQueue;

        public EnqueueEncryptedChunksForUploadBlockProvider AddUploadQueue(BlockingCollection<EncryptedChunkFile> uploadQueue)
        {
            _uploadQueue = uploadQueue;

            return this;
        }

        public ActionBlock<EncryptedChunkFile> GetBlock()
        {
            return new(item => _uploadQueue.Add(item));
        }
    }

    internal class EnqueueUploadTaskProvider
    {
        private readonly IConfiguration _config;

        public EnqueueUploadTaskProvider(IConfiguration config)
        {
            _config = config;
        }

        private BlockingCollection<EncryptedChunkFile> _uploadQueue;
        private ITargetBlock<EncryptedChunkFile[]> _uploadEncryptedChunksBlock;
        private ActionBlock<EncryptedChunkFile> _enqueueEncryptedChunksForUploadBlock;

        //private const int AzCopyBatchSize = 256 * 1024 * 1024; //256 MB
        //private const int AzCopyBatchCount = 128;

        public EnqueueUploadTaskProvider AddUploadQueue(BlockingCollection<EncryptedChunkFile> uploadQueue)
        {
            _uploadQueue = uploadQueue;

            return this;
        }

        public EnqueueUploadTaskProvider AddUploadEncryptedChunkBlock(ITargetBlock<EncryptedChunkFile[]> uploadEncryptedChunksBlock)
        {
            _uploadEncryptedChunksBlock = uploadEncryptedChunksBlock;

            return this;
        }

        public EnqueueUploadTaskProvider AddEnqueueEncryptedChunksForUploadBlock(ActionBlock<EncryptedChunkFile> enqueueEncryptedChunksForUploadBlock)
        {
            _enqueueEncryptedChunksForUploadBlock = enqueueEncryptedChunksForUploadBlock;

            return this;
        }

        public Task GetTask()
        {
            return Task.Run(() =>
            {
                Thread.CurrentThread.Name = "Upload Batcher";

                while (!_enqueueEncryptedChunksForUploadBlock.Completion.IsCompleted ||
                       //encryptChunksBlock.OutputCount > 0 || 
                       _uploadQueue.Count > 0)
                {
                    var uploadBatch = new List<EncryptedChunkFile>();
                    long size = 0;
                    foreach (var ecf in _uploadQueue.GetConsumingEnumerable()) //TODO DIT KLOPT NIET gaat gewoon heel de queue uitlezen
                    {
                        uploadBatch.Add(ecf);
                        size += ecf.Length;
                    }

                    if (size >= _config.BatchSize ||
                        uploadBatch.Count >= _config.BatchCount ||
                        _uploadQueue.IsCompleted) //if we re at the end of the queue, upload the remainder
                    {
                        _uploadEncryptedChunksBlock.Post(uploadBatch.ToArray());
                        break;
                    }
                }
            });
        }
    }

    internal class UploadEncryptedChunksBlockProvider
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
            return new(encryptedChunkFiles =>
                {
                    //Upload the files
                    var uploadedBlobs = _azureRepository.Upload(encryptedChunkFiles, _options.Tier);

                    //Delete the files
                    foreach (var encryptedChunkFile in encryptedChunkFiles)
                        encryptedChunkFile.Delete();

                    return uploadedBlobs.Select(recbi => recbi.Hash);
                }, new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 2});
        }
    }

    internal class ReconcileChunksWithManifestsBlockProvider
    {
        private Dictionary<BinaryFile, List<HashValue>> _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;

        public ReconcileChunksWithManifestsBlockProvider AddChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(
            Dictionary<BinaryFile, List<HashValue>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
        {
            _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;

            return this;
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

    internal class CreateManifestBlockProvider
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

    internal class ReconcileBinaryFilesWithManifestBlockProvider
    {
        private List<HashValue> _uploadedManifestHashes;
        private Dictionary<HashValue, List<BinaryFile>> _binaryFilesPerManifestHash;

        public ReconcileBinaryFilesWithManifestBlockProvider AddUploadedManifestHashes(List<HashValue> uploadedManifestHashes)
        {
            _uploadedManifestHashes = uploadedManifestHashes;
            _binaryFilesPerManifestHash = new Dictionary<HashValue, List<BinaryFile>>(); //Key = HashValue van de Manifest

            return this;
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

    internal class CreatePointerBlockProvider
    {
        private readonly PointerService _ps;
        private List<BinaryFile> _binaryFilesToDelete;

        public CreatePointerBlockProvider(PointerService ps)
        {
            _ps = ps;
        }

        public CreatePointerBlockProvider AddBinaryFilesToDelete(List<BinaryFile> binaryFilesToDelete)
        {
            _binaryFilesToDelete = binaryFilesToDelete;

            return this;
        }

        public TransformBlock<BinaryFile, PointerFile> GetBlock()
        {
            return new(binaryFile =>
            {
                // Create the pointer
                var p = _ps.CreatePointerFileIfNotExists(binaryFile);

                // Add the binary file to the list of binaries to be deleted after successful archiving & if !keepLocal
                _binaryFilesToDelete.Add(binaryFile);
                return p;
            });
        }
    }

    internal class CreatePointerFileEntryIfNotExistsBlockProvider
    {
        private readonly ILogger<CreatePointerFileEntryIfNotExistsBlockProvider> _logger;
        private readonly AzureRepository _azureRepository;
        private DateTime _version;

        public CreatePointerFileEntryIfNotExistsBlockProvider(ILogger<CreatePointerFileEntryIfNotExistsBlockProvider> logger, AzureRepository azureRepository)
        {
            _logger = logger;
            _azureRepository = azureRepository;
        }

        public CreatePointerFileEntryIfNotExistsBlockProvider AddVersion(DateTime version)
        {
            _version = version;

            return this;
        }

        public ActionBlock<PointerFile> GetBlock()
        {
            return new(async pointerFile =>
            {
                await _azureRepository.CreatePointerFileEntryIfNotExistsAsync(pointerFile, _version);
            });
        }
    }

    internal class RemoveDeletedPointersTaskProvider
    {
        private readonly ILogger<RemoveDeletedPointersTaskProvider> _logger;
        private readonly AzureRepository _azureRepository;
        private readonly DirectoryInfo _root;
        private DateTime _version;


        public RemoveDeletedPointersTaskProvider(ILogger<RemoveDeletedPointersTaskProvider> logger, ArchiveOptions options, AzureRepository azureRepository)      
        {
            _logger = logger;
            _azureRepository = azureRepository;

            _root = new DirectoryInfo(options.Path);
        }

        public RemoveDeletedPointersTaskProvider AddVersion(DateTime version)
        {
            _version = version;

            return this;
        }

        public Task GetTask()
        {
            return new (async () =>
            {
                var pfes = await _azureRepository.GetCurrentEntriesAsync(true);
                pfes = pfes.Where(e => e.Version < _version).ToList(); // that were not created in the current run (those are assumed to be up to date)

                Parallel.ForEach(pfes, async pfe =>
                {
                    var pointerFullName = Path.Combine(_root.FullName, pfe.RelativeName);
                    if (!File.Exists(pointerFullName) && !pfe.IsDeleted)
                        await _azureRepository.CreatePointerFileEntryIfNotExistsAsync(pfe, _version, true);
                });
            });
        }
    }

    internal class ExportToJsonTaskProvider
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
            return new(() => 
            {
                //using Stream file = File.Create(@"c:\ha.json");

                //var json = new Utf8JsonWriter(file, new JsonWriterOptions() { Indented = true });

                //json.WriteStartObject();


                //foreach (var pfe in await _azureRepository.GetCurrentEntries(false))
                //{

                //    json.WriteStartObject("ha");

                //    var zz = JsonSerializer.Serialize(pfe, new JsonSerializerOptions() { WriteIndented = true });
                //    var x = JsonEncodedText.Encode(zz, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
                //    json.WriteString(JsonEncodedText.Encode("he"), x);

                //    //json.WriteNumber("age", 15, escape: false);
                //    //json.WriteString("date", DateTime.Now);
                //    //json.WriteString("first", "John");
                //    //json.WriteString("last", "Smith");

                //    //json.WriteStartArray("phoneNumbers", escape: false);
                //    //json.WriteStringValue("425-000-1212", escape: false);
                //    //json.WriteStringValue("425-000-1213");
                //    //json.WriteEndArray();

                //    //json.WriteStartObject("address");
                //    //json.WriteString("street", "1 Microsoft Way");
                //    //json.WriteString("city", "Redmond");
                //    //json.WriteNumber("zip", 98052);
                //    //json.WriteEndObject();

                //    //json.WriteStartArray("ExtraArray");
                //    //for (var i = 0; i < extraData.Length; i++)
                //    //{
                //    //    json.WriteNumberValue(extraData[i]);
                //    //}
                //    //json.WriteEndArray();

                //    //json.WriteEndObject();

                //    //json.Flush(isFinalBlock: true);

                //    //return (int)json.BytesWritten;



                //    //Stream stream = ...;

                //    //using (var streamWriter = new StreamWriter(stream))
                //    //using (var writer = new JsonTextWriter(streamWriter))
                //    //{
                //    //    writer.Formatting = Formatting.Indented;

                //    //    writer.WriteStartArray();
                //    //    {
                //    //        writer.WriteStartObject();
                //    //        {
                //    //            writer.WritePropertyName("foo");
                //    //            writer.WriteValue(1);
                //    //            writer.WritePropertyName("bar");
                //    //            writer.WriteValue(2.3);
                //    //        }
                //    //        writer.WriteEndObject();
                //    //    }
                //    //    writer.WriteEndArray();
                //    //}

                //    json.WriteEndObject();


                //}
                ////await JsonSerializer.SerializeAsync(file, _azureRepository.GetAllManifestEntriesWithChunksAndPointerFileEntries() ,
                ////    new JsonSerializerOptions {WriteIndented = true});

                //json.WriteEndObject();


                //json.Flush();

            });
        }
    }

    internal class DeleteBinaryFilesTaskProvider
    {
        public DeleteBinaryFilesTaskProvider(ArchiveOptions options)
        {
            _options = options;
        }

        private readonly ArchiveOptions _options;
        private List<BinaryFile> _binaryFilesToDelete;

        public DeleteBinaryFilesTaskProvider AddBinaryFilesToDelete(List<BinaryFile> binaryFilesToDelete)
        {
            _binaryFilesToDelete = binaryFilesToDelete;

            return this;
        }

        public Task GetTask()
        {
            return new(() =>
            {
                if (_options.KeepLocal)
                    return;

                Parallel.ForEach(_binaryFilesToDelete, bf => bf.Delete());
            });
        }
    }
}