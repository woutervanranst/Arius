﻿using System;
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

        public TransformManyBlock<DirectoryInfo, AriusArchiveItem> GetBlock()
        {
            return new(di => IndexDirectory(_root),
                new ExecutionDataflowBlockOptions { SingleProducerConstrained = true }
            );
        }

        private IEnumerable<AriusArchiveItem> IndexDirectory(DirectoryInfo root)
        {
            _logger.LogInformation($"Indexing {root.FullName}");

            foreach (var fi in root.GetFiles("*", SearchOption.AllDirectories).AsParallel())
            {
                if (fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase))
                {
                    _logger.LogInformation($"Found PointerFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

                    yield return new PointerFile(root, fi);
                }
                else
                {
                    _logger.LogInformation($"Found BinaryFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

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

        public TransformBlock<AriusArchiveItem, AriusArchiveItem> GetBlock()
        {
            return new(item =>
            {
                if (item is PointerFile pf)
                    return pf;
                else if (item is BinaryFile bf)
                {
                    _logger.LogInformation($"Hashing BinaryFile {bf.RelativeName}");

                    bf.Hash = _hvp.GetHashValue(bf);

                    _logger.LogInformation($"Hashing BinaryFile {bf.RelativeName} done");

                    return bf;
                }
                else
                    throw new ArgumentException($"Cannot add hash to item of type {item.GetType().Name}");
            },
            new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, BoundedCapacity = 15 });
        }
    }



    internal abstract class ProcessIfNotExistBlocksProvider<T> where T : IFileWithHash
    {
            public ProcessIfNotExistBlocksProvider(IEnumerable<HashValue> createdInital)
            {
                _created = new(createdInital);
            }

            private readonly List<HashValue> _created;
            private readonly Dictionary<HashValue, List<T>> _creating = new();


            public TransformManyBlock<T, (T Item, bool ToProcess)> GetCreateIfNotExistsBlock()
            {
                /*
                 * Three possibilities:
                 *      1. BinaryFile arrives, remote manifest already exists --> send to next block
                 *      2. BinaryFile arrives, remote manifest does not exist and is not being created --> send to the creation pipe
                 *      3. BinaryFile arrives, remote manifest does not exist and IS beign created --> add to the waiting pipe
                 */
                return new(item =>
                {
                    lock (_created)
                    {
                        lock (_creating)
                        {
                            if (_created.Contains(item.Hash))
                                // 1 - Exists remote
                                return new[] { (item, false) };
                            else if (!_creating.ContainsKey(item.Hash))
                            {
                                // 2 Does not yet exist remote and not yet being created --> upload
                                _creating.Add(item.Hash, new());
                                _creating[item.Hash].Add(item);

                                return new[] { (item, true), (item, false) };
                            }
                            else
                            {
                                // 3 Does not exist remote but is being created
                                _creating[item.Hash].Add(item);

                                return new[] { (item, false) };
                            }
                        }
                    }
                });
            }

            public TransformManyBlock<object, T> GetReconcileBlock()
            {
                return new(item =>
                {
                    lock (_created)
                    {
                        lock (_creating)
                        {
                            if (item is T bf)
                            {
                                if (_created.Contains(bf.Hash))
                                    return new[] { bf }; // Manifest already uploaded
                                else if (_creating.ContainsKey(bf.Hash))
                                    // it is alreayd in de _pending list // do nothing
                                    return Enumerable.Empty<T>();
                                else
                                    throw new InvalidOperationException("huh??");
                            }
                            else if (item is HashValue completedManifestHash)
                            {
                                _created.Add(completedManifestHash); // add to the list of uploaded hashes

                                var r = _creating[completedManifestHash].ToArray();
                                _creating.Remove(completedManifestHash);

                                return r;
                            }
                            else
                                throw new ArgumentException();
                        }
                    }
                });
            }
    }


    internal class ManifestBlocksProvider : ProcessIfNotExistBlocksProvider<BinaryFile>
    {
        public ManifestBlocksProvider(AzureRepository repo) : base(repo.GetAllManifestHashes())
        {
        }
    }

    //internal abstract class ProcessManyIfNotExistBlocksProvider<TKey, TElement> where TKey : IFileWithHash 
    //                                                                            where TElement : IFileWithHash
    //{
    //    public ProcessManyIfNotExistBlocksProvider(IEnumerable<HashValue> createdInital)
    //    {
    //        _created = new(createdInital);
    //    }

    //    private readonly List<HashValue> _created; //HashValue = Chunk.Hash
    //    private readonly Dictionary<HashValue, (List<BinaryFile> BinaryFiles, List<IChunkFile> ChunkFiles)> _creating = new(); //HashValue = BinaryFile.Hash

    //    public TransformManyBlock<(IChunkFile Chunk, BinaryFile BelongsTo), (IChunkFile Chunk, BinaryFile BelongsTo, bool Process)> GetCreateIfNotExistsBlock()
    //    {
    //        /*
    //         * Three possibilities:
    //         *      1. BinaryFile arrives, remote manifest already exists --> send to next block
    //         *      2. BinaryFile arrives, remote manifest does not exist and is not being created --> send to the creation pipe
    //         *      3. BinaryFile arrives, remote manifest does not exist and IS beign created --> add to the waiting pipe
    //         */
    //        return new(item =>
    //        {
    //            lock (_created)
    //            {
    //                lock (_creating)
    //                {
    //                    if (_created.Contains(item.Chunk.Hash))
    //                        // 1 - Exists remote
    //                        return Enumerable.Empty<(IChunkFile, BinaryFile, bool)>(); // new[] { (item.Chunk, false) };
    //                    else if (!_creating.ContainsKey(item.BelongsTo.Hash))
    //                    {
    //                        // 2 Does not yet exist remote and not yet being created --> upload
    //                        _creating.Add(item.BelongsTo.Hash, new());
    //                        _creating[item.BelongsTo.Hash].BinaryFiles.Add(item.BelongsTo);
    //                        _creating[item.BelongsTo.Hash].ChunkFiles.Add(item.BelongsTo);

    //                        return new[] { (item.Chunk, true), (item.Chunk, false) };
    //                    }
    //                    else
    //                    {
    //                        // 3 Does not exist remote but is being created
    //                        _creating[item.BelongsTo].Add(item.Chunk);

    //                        return new[] { (item.Chunk, false) };
    //                    }
    //                }
    //            }
    //        });
    //    }

    //    public TransformManyBlock<object, BinaryFile> GetReconcileBlock()
    //    {
    //        return new(item =>
    //        {
    //            lock (_created)
    //            {
    //                lock (_creating)
    //                {
    //                    if (item is IChunkFile bf)
    //                    {
    //                        if (_created.Contains(bf.Hash))
    //                            return new[] { bf }; // Manifest already uploaded
    //                        else if (_creating.ContainsKey(bf.Hash))
    //                            // it is alreayd in de _pending list // do nothing
    //                            return Enumerable.Empty<IChunkFile>();
    //                        else
    //                            throw new InvalidOperationException("huh??");
    //                    }
    //                    else if (item is HashValue completedManifestHash)
    //                    {
    //                        _created.Add(completedManifestHash); // add to the list of uploaded hashes

    //                        var r = _creating[completedManifestHash].ToArray();
    //                        _creating.Remove(completedManifestHash);

    //                        return r;
    //                    }
    //                    else
    //                        throw new ArgumentException();
    //                }
    //            }
    //        });
    //    }
    //}

    //internal class ChunkBlocksProvider : ProcessManyIfNotExistBlocksProvider<BinaryFile, ChunkFile>
    //{
    //    public ChunkBlocksProvider(AzureRepository repo, IChunker chunker) : base(repo.GetAllChunkBlobItems().Select(recbi => recbi.Hash).ToList())
    //    {
    //        _chunker = chunker;
    //    }

    //    private readonly IChunker _chunker;

    //    public TransformManyBlock<BinaryFile, (IChunkFile Chunk, BinaryFile BelongsTo)> GetChunkBlock()
    //    {
    //        return new(bf =>
    //        {
    //            Console.WriteLine("Chunking BinaryFile " + bf.Name);

    //            var cs = _chunker.Chunk(bf).ToArray();
    //            //bf.Chunks = cs;

    //            Console.WriteLine("Chunking BinaryFile " + bf.Name + " done");

    //            return cs.Select(c => (c, bf));
    //        });
    //    }
    //}

    internal class ChunkBlockProvider
    {
        public ChunkBlockProvider(IChunker chunker,
            AzureRepository azureRepository)
        {
            _chunker = chunker;

            _uploadedOrUploadingChunks = azureRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).ToList();
        }

        private readonly IChunker _chunker;
        private readonly List<HashValue> _uploadedOrUploadingChunks;

        private Dictionary<BinaryFile, List<HashValue>> _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;


        public ChunkBlockProvider SetChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(Dictionary<BinaryFile, List<HashValue>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
        {
            _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;

            return this;
        }

        public TransformManyBlock<BinaryFile, (IChunkFile ChunkFile, bool Uploaded)> GetBlock()
        {
            return new(binaryFile => 
            {
                var chunks = AddChunks(binaryFile);

                var r = chunks.Select(chunk =>
                {
                    bool uploaded;

                    lock (_uploadedOrUploadingChunks)
                    {
                        if (_uploadedOrUploadingChunks.Contains(chunk.Hash))
                        {
                            //if (chunk is BinaryFile)
                            //    throw new InvalidOperationException();

                            if (chunk is ChunkFile)
                                chunk.Delete(); //The chunk is already uploaded, delete it

                            uploaded = true;
                        }
                        else
                        {
                            lock (_chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                            {
                                if (!_chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.ContainsKey(binaryFile))
                                    _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.Add(binaryFile, new List<HashValue>(new[] { chunk.Hash }));
                                else
                                    _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated[binaryFile].Add(chunk.Hash);
                            }

                            uploaded = false; //ie to upload
                        }

                        _uploadedOrUploadingChunks.Add(chunk.Hash);
                    }

                    return (ChunkFile: chunk, Uploaded: uploaded);
                });

                return r;
            }, new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = Environment.ProcessorCount });
        }

        private IEnumerable<IChunkFile> AddChunks(BinaryFile f)
        {
            //Console.WriteLine("Chunking BinaryFile " + f.Name);

            var cs = _chunker.Chunk(f).ToArray();
            f.Chunks = cs;

            //Console.WriteLine("Chunking BinaryFile " + f.Name + " done");

            return cs;
        }
    }

    internal class EncryptChunksBlockProvider
    {
        public EncryptChunksBlockProvider(ILogger<EncryptChunksBlockProvider> logger, IConfiguration config, IEncrypter encrypter)
        {
            _logger = logger;
            _config = config;
            _encrypter = encrypter;
        }

        private readonly ILogger<EncryptChunksBlockProvider> _logger;
        private readonly IConfiguration _config;
        private readonly IEncrypter _encrypter;

        public TransformBlock<IChunkFile, EncryptedChunkFile> GetBlock()
        {
            return new(
                chunkFile => EncryptChunks(chunkFile),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 8});
        }

        private EncryptedChunkFile EncryptChunks(IChunkFile f)
        {
            _logger.LogInformation($"Encrypting ChunkFile {f.Name}");

            var targetFile = new FileInfo(Path.Combine(_config.UploadTempDir.FullName, "encryptedchunks", $"{f.Hash}{EncryptedChunkFile.Extension}"));

            _encrypter.Encrypt(f, targetFile, SevenZipCommandlineEncrypter.Compression.NoCompression, f is not BinaryFile);

            var ecf = new EncryptedChunkFile(f.Root, targetFile, f.Hash);

            _logger.LogInformation($"Encrypting ChunkFile {f.Name} done");

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
                       //_uploadQueue.Count > 0)
                       !_uploadQueue.IsCompleted)
                {
                    var batch = new List<EncryptedChunkFile>();
                    long size = 0;

                    foreach (var ecf in _uploadQueue.GetConsumingEnumerable())
                    {
                        batch.Add(ecf);
                        size += ecf.Length;

                        if (size >= _config.BatchSize ||
                            batch.Count >= _config.BatchCount ||
                            _uploadQueue.IsCompleted) //if we re at the end of the queue, upload the remainder
                        {
                            break;
                        }
                    }

                    //Emit a batch
                    if (batch.Any())
                        _uploadEncryptedChunksBlock.Post(batch.ToArray());
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
            return new(ecfs =>
                {
                    //Upload the files
                    var uploadedBlobs = _azureRepository.Upload(ecfs, _options.Tier);

                    //Delete the files
                    foreach (var ecf in ecfs)
                        ecf.Delete();

                    return uploadedBlobs.Select(recbi => recbi.Hash);
                }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 2 });
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
                                //kvp.Key.ManifestHash = kvp.Key.Hash;
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
        public CreateManifestBlockProvider(AzureRepository azureRepository)
        {
            _azureRepository = azureRepository;
        }

        private readonly AzureRepository _azureRepository;

        public TransformBlock<BinaryFile, object> GetBlock()
        {
            return new(async bf =>
            {
                await _azureRepository.AddManifestAsync(bf, bf.Chunks.ToArray());

                return bf.Hash;
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

                // Add the binary file to the list of binaries to be deleted after successful archiving & if removeLocal
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
        public RemoveDeletedPointersTaskProvider(ILogger<RemoveDeletedPointersTaskProvider> logger, ArchiveOptions options, AzureRepository azureRepository)      
        {
            _logger = logger;
            _azureRepository = azureRepository;

            _root = new DirectoryInfo(options.Path);
        }

        private readonly ILogger<RemoveDeletedPointersTaskProvider> _logger;
        private readonly AzureRepository _azureRepository;
        private readonly DirectoryInfo _root;
        private DateTime _version;

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
                if (!_options.RemoveLocal)
                    return;

                Parallel.ForEach(_binaryFilesToDelete, bf => bf.Delete());
            });
        }
    }
}