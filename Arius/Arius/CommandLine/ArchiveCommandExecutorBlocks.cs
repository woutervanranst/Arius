using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.Extensions.Logging;

namespace Arius.CommandLine
{
    internal class IndexDirectoryBlockProvider
    {
        private readonly ILogger<IndexDirectoryBlockProvider> _logger;

        public IndexDirectoryBlockProvider(ILogger<IndexDirectoryBlockProvider> logger)
        {
            _logger = logger;
        }

        public TransformManyBlock<DirectoryInfo, IAriusEntry> GetBlock()
        {
            return new(di =>
            {
                try
                {
                    _logger.LogInformation($"Indexing {di.FullName}");

                    return IndexDirectory2(di);
                    //var x = GetAllFiles(di).ToList();

                    //return IndexDirectory(di);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO", di);
                    throw;
                }
            },
                new ExecutionDataflowBlockOptions { SingleProducerConstrained = true }
            );
        }

        private IEnumerable<IAriusEntry> IndexDirectory(DirectoryInfo root)
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

        /// <summary>
        /// (new implemenation that excludes system/hidden files (eg .git / @eaDir)
        /// </summary>
        /// <param name="directory"></param>
        /// <returns></returns>
        private IEnumerable<IAriusEntry> IndexDirectory2([NotNull] DirectoryInfo directory)
        {
            foreach (var file in directory.GetFiles())
            {
                _logger.LogDebug("ATTRIBUTES " + file.FullName + ": " + file.Attributes.ToString());

                if (IsHiddenOrSystem(file.Attributes))
                {
                    _logger.LogDebug($"Skipping file {file.FullName} as it is SYSTEM or HIDDEN");
                    continue;
                }
                else
                { 
                    yield return GetAriusEntry(directory, file);
                }
            }

            foreach (var dir in directory.GetDirectories())
            {
                _logger.LogDebug("ATTRIBUTES " + directory.FullName + ": " + dir.Attributes.ToString());


                if (IsHiddenOrSystem(dir.Attributes))
                {
                    _logger.LogDebug($"Skipping directory {dir.FullName} as it is SYSTEM or HIDDEN");
                    continue;
                }

                foreach (var f in IndexDirectory2(dir))
                    yield return f;
            }
        }

        private bool IsHiddenOrSystem(FileAttributes attr)
        {
            return (attr & FileAttributes.System) != 0 || (attr & FileAttributes.Hidden) != 0;
        }

        private IAriusEntry GetAriusEntry(DirectoryInfo root, FileInfo fi)
        {
            if (fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase))
            {
                _logger.LogInformation($"Found PointerFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

                return new PointerFile(root, fi);
            }
            else
            {
                _logger.LogInformation($"Found BinaryFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

                return new BinaryFile(root, fi);
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

        public TransformBlock<IAriusEntry, IAriusEntryWithHash> GetBlock()
        {
            return new(item =>
            {
                try
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
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO", item);
                    throw;
                }
            },
            new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, BoundedCapacity = 15 });
        }
    }



    internal abstract class ProcessIfNotExistBlocksProvider<T> where T : IFile, IWithHashValue
    {
        protected ProcessIfNotExistBlocksProvider(ILogger logger, IEnumerable<HashValue> createdInital)
        {
            _logger = logger;
            _created = new(createdInital);
        }

        private readonly ILogger _logger;
        private readonly List<HashValue> _created;
        private readonly Dictionary<HashValue, List<T>> _creating = new();


        public TransformManyBlock<T, (T Item, bool ToProcess)> GetCreateIfNotExistsBlock()
        {
            /* 
             * Three possibilities:
             *      1. BinaryFile arrives, remote manifest already exists --> send to next block //TODO explain WHY
             *      2. BinaryFile arrives, remote manifest does not exist and is not being created --> send to the creation pipe
             *      3. BinaryFile arrives, remote manifest does not exist and IS beign created --> add to the waiting pipe
             */
            return new(item =>
            {
                try
                {
                    lock (_created)
                    {
                        lock (_creating)
                        {
                            if (_created.Contains(item.Hash))
                            {
                                // 1 - Exists remote
                                _logger.LogInformation($"GetCreateIfNotExistsBlock - {typeof(T).Name} {item.Name} already exists. No need to process.");

                                return new[] { (item, false) };
                            }
                            else if (!_creating.ContainsKey(item.Hash))
                            {
                                // 2 Does not yet exist remote and not yet being created --> upload
                                _logger.LogInformation($"GetCreateIfNotExistsBlock - {typeof(T).Name} {item.Name} does not exist remotly. To process.");

                                _creating.Add(item.Hash, new());
                                _creating[item.Hash].Add(item);
                                return new[] { (item, true), (item, false) };
                            }
                            else
                            {
                                // 3 Does not exist remote but is being created
                                _logger.LogInformation($"GetCreateIfNotExistsBlock - {typeof(T).Name} {item.Name} does not exist yet is being processed.");

                                _creating[item.Hash].Add(item);

                                return new[] { (item, false) };
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO", item);
                    throw;
                }
        });
        }

        public TransformManyBlock<object, T> GetReconcileBlock()
        {
            return new(item =>
            {
                try
                {
                    lock (_created)
                    {
                        lock (_creating)
                        {
                            if (item is T bf)
                            {
                                if (_created.Contains(bf.Hash))
                                {
                                    _logger.LogInformation($"GetReconcileBlock - {typeof(T).Name} {bf.Name} already created. Passing item to next block.");

                                    return new[] { bf }; // Manifest already uploaded
                                }
                                else if (_creating.ContainsKey(bf.Hash))
                                {
                                    _logger.LogInformation($"GetReconcileBlock - {typeof(T).Name} {bf.Name} in the pending list. Waiting for reconciliation.");

                                    // it is alreayd in de _pending list // do nothing
                                    return Enumerable.Empty<T>();
                                }
                                    
                                else
                                    throw new InvalidOperationException("huh??");
                            }
                            else if (item is HashValue completedManifestHash)
                            {
                                _logger.LogInformation($"GetReconcileBlock - Reconciling completed manifesthash {completedManifestHash}.");

                                _created.Add(completedManifestHash); // add to the list of uploaded hashes

                                var r = _creating[completedManifestHash].ToArray();
                                _creating.Remove(completedManifestHash);

                                _logger.LogInformation($"GetReconcileBlock - Passing {r.Length} items to next block");

                                return r;
                            }
                            else
                                throw new ArgumentException();
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO", item);
                    throw;
                }
            });
        }
    }


    internal class ManifestBlocksProvider : ProcessIfNotExistBlocksProvider<BinaryFile>
    {
        public ManifestBlocksProvider(ILogger<CreateManifestBlockProvider> logger, AzureRepository repo) : base(logger, repo.GetAllManifestHashes())
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
        public ChunkBlockProvider(ILogger<ChunkBlockProvider> logger, IChunker chunker, AzureRepository azureRepository)
        {
            _logger = logger;
            _chunker = chunker;
            this.azureRepository = azureRepository;
            _uploadedOrUploadingChunks = azureRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).ToList();
        }

        private readonly ILogger<ChunkBlockProvider> _logger;
        private readonly IChunker _chunker;
        private readonly AzureRepository azureRepository;
        private readonly List<HashValue> _uploadedOrUploadingChunks;

        private Dictionary<BinaryFile, List<HashValue>> _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;


        public ChunkBlockProvider SetChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(Dictionary<BinaryFile, List<HashValue>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
        {
            _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;

            return this;
        }

        /// <summary>
        /// IN: BinaryFile for which the Manifest does not exist
        /// OUT: a list of ChunkFiles with each an indication of whethery they need to be uploaded
        /// </summary>
        /// <returns></returns>
        public TransformManyBlock<BinaryFile, (IChunkFile ChunkFile, bool Uploaded)> GetBlock()
        {
            return new(bf =>
            {
                try
                {
                    _logger.LogInformation($"Chunking BinaryFile {bf.RelativeName}...");
                    var chunks = AddChunks(bf);
                    _logger.LogInformation($"Chunking BinaryFile {bf.RelativeName}... in {chunks.Count()} chunks");

                    lock (_uploadedOrUploadingChunks)
                    {
                        lock (_chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                        {
                            if (!_chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.ContainsKey(bf))
                                _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.Add(bf, new());

                            var r = chunks.Select(chunk =>
                            {
                                bool uploaded;

                                if (_uploadedOrUploadingChunks.Contains(chunk.Hash))
                                {
                                    _logger.LogInformation($"Chunk {chunk.FullName} is already uploaded.");

                                    if (chunk is ChunkFile)
                                        chunk.Delete(); //The chunk is already uploaded, delete it. Do not delete a binaryfile at this stage.

                                    uploaded = true;
                                }
                                else
                                {
                                    _logger.LogInformation($"Chunk {chunk.FullName} to be uploaded.");

                                    uploaded = false; //ie to upload
                                    _uploadedOrUploadingChunks.Add(chunk.Hash); //add this chunk to the list of chunks that (will be/is) present in the archive
                                }

                                if (!uploaded)
                                    _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated[bf].Add(chunk.Hash); //Add this chunk to the list of chunks that need to be uploaded before the manifest can be created

                                return (ChunkFile: chunk, Uploaded: uploaded);
                            });

                            return r;
                        }
                    }
                }
                catch (Exception e)
                {
                    //TODO ADD LOGGING IN TRY BLOCK
                    _logger.LogError(e, "ERRORTODO", bf);
                    throw;
                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount });
        }

        private IEnumerable<IChunkFile> AddChunks(BinaryFile bf)
        {
            //Console.WriteLine("Chunking BinaryFile " + f.Name);

            var cs = _chunker.Chunk(bf).ToArray();
            bf.Chunks = cs;

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
            return new(chunkFile =>
                {
                    try
                    {
                        _logger.LogInformation($"Encrypting ChunkFile {chunkFile.Name}");

                        var targetFile = new FileInfo(Path.Combine(_config.UploadTempDir.FullName, "encryptedchunks", $"{chunkFile.Hash}{EncryptedChunkFile.Extension}"));

                        _encrypter.Encrypt(chunkFile, targetFile, SevenZipCommandlineEncrypter.Compression.NoCompression, chunkFile is not BinaryFile);

                        var ecf = new EncryptedChunkFile(targetFile, chunkFile.Hash);

                        _logger.LogInformation($"Encrypting ChunkFile {chunkFile.Name} done");

                        return ecf;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "ERRORTODO", chunkFile);
                        throw;
                    }
                },
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 8 });
        }
    }

    internal class EnqueueEncryptedChunksForUploadBlockProvider
    {
        public EnqueueEncryptedChunksForUploadBlockProvider(ILogger<EnqueueEncryptedChunksForUploadBlockProvider> logger)
        {
            _logger = logger;
        }

        private BlockingCollection<EncryptedChunkFile> _uploadQueue;
        private readonly ILogger<EnqueueEncryptedChunksForUploadBlockProvider> _logger;

        public EnqueueEncryptedChunksForUploadBlockProvider AddUploadQueue(BlockingCollection<EncryptedChunkFile> uploadQueue)
        {
            _uploadQueue = uploadQueue;

            return this;
        }

        public ActionBlock<EncryptedChunkFile> GetBlock()
        {
            return new(item =>
            {
                try
                {
                    _logger.LogInformation($"Enqueueing item {item.Hash} for upload. Queue length: {_uploadQueue.Count}");

                    _uploadQueue.Add(item);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO", item);
                    throw;
                }
            });
        }
    }

    internal class CreateUploadBatchesTaskProvider
    {
        public CreateUploadBatchesTaskProvider(ILogger<CreateUploadBatchesTaskProvider> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        private readonly ILogger<CreateUploadBatchesTaskProvider> _logger;
        private readonly IConfiguration _config;

        public CreateUploadBatchesTaskProvider AddUploadQueue(BlockingCollection<EncryptedChunkFile> uploadQueue)
        {
            _uploadQueue = uploadQueue;
            return this;
        }
        private BlockingCollection<EncryptedChunkFile> _uploadQueue;

        public CreateUploadBatchesTaskProvider AddUploadEncryptedChunkBlock(ITargetBlock<EncryptedChunkFile[]> uploadEncryptedChunksBlock)
        {
            _uploadEncryptedChunksBlock = uploadEncryptedChunksBlock;
            return this;
        }
        private ITargetBlock<EncryptedChunkFile[]> _uploadEncryptedChunksBlock;

        public CreateUploadBatchesTaskProvider AddEnqueueEncryptedChunksForUploadBlock(ActionBlock<EncryptedChunkFile> enqueueEncryptedChunksForUploadBlock)
        {
            _enqueueEncryptedChunksForUploadBlock = enqueueEncryptedChunksForUploadBlock;
            return this;
        }
        private ActionBlock<EncryptedChunkFile> _enqueueEncryptedChunksForUploadBlock;


        public Task GetTask()
        {
            return Task.Run(() =>
            {
                try
                {
                    Thread.CurrentThread.Name = "Upload Batcher";

                    while (!_enqueueEncryptedChunksForUploadBlock.Completion.IsCompleted ||
                           //encryptChunksBlock.OutputCount > 0 || 
                           //_uploadQueue.Count > 0)
                           !_uploadQueue.IsCompleted)
                    {
                        var batch = new List<EncryptedChunkFile>();
                        long size = 0;

                        _logger.LogInformation("Starting new upload batch");

                        foreach (var ecf in _uploadQueue.GetConsumingEnumerable())
                        {
                            batch.Add(ecf);
                            size += ecf.Length;

                            _logger.LogInformation($"Added {ecf.Hash} to the batch. Batch Count: {batch.Count}, Batch Size: {size.GetBytesReadable()}, Remaining Queue Count: {_uploadQueue.Count}");

                            if (size >= _config.BatchSize ||
                                batch.Count >= _config.BatchCount ||
                                _uploadQueue.IsCompleted) //if we re at the end of the queue, upload the remainder
                            {
                                break;
                            }
                        }

                        //Emit a batch
                        if (batch.Any())
                        {
                            _logger.LogInformation($"Emitting batch for upload. Batch Size: {size.GetBytesReadable()}, Batch Count: {batch.Count}, Batches Queue depth: {((dynamic)_uploadEncryptedChunksBlock).InputCount}");
                            _uploadEncryptedChunksBlock.Post(batch.ToArray());
                        }
                    }

                    _logger.LogInformation($"Done creating batches for upload. UploadQueue Count: {_uploadQueue.Count} (should be 0), IsCompleted:{_uploadQueue.IsCompleted}");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO", _enqueueEncryptedChunksForUploadBlock, _uploadQueue);
                    throw;
                }
            });
        }
    }

    internal class UploadEncryptedChunksBlockProvider
    {
        public UploadEncryptedChunksBlockProvider(ILogger<UploadEncryptedChunksBlockProvider> logger, ArchiveOptions options, AzureRepository azureRepository)
        {
            _logger = logger;
            _options = options;
            _azureRepository = azureRepository;

            _block = new(InitBlock());
        }

        private readonly ILogger<UploadEncryptedChunksBlockProvider> _logger;
        private readonly ArchiveOptions _options;
        private readonly AzureRepository _azureRepository;

        public TransformManyBlock<EncryptedChunkFile[], HashValue> InitBlock()
        {
            return new(ecfs =>
            {
                try
                {
                    _logger.LogInformation($"Uploading batch. Remaining Batches queue depth: {_block!.Value.InputCount}");

                    //Upload the files
                    var uploadedBlobs = _azureRepository.Upload(ecfs, _options.Tier);

                    //Delete the files
                    foreach (var ecf in ecfs)
                        ecf.Delete();

                    return uploadedBlobs.Select(recbi => recbi.Hash);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO", ecfs);
                    throw;
                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 2 });
        }
        private readonly Lazy<TransformManyBlock<EncryptedChunkFile[], HashValue>> _block;

        public TransformManyBlock<EncryptedChunkFile[], HashValue> GetBlock() => _block.Value;
    }


    internal class ReconcileChunksWithManifestsBlockProvider
    {
        public ReconcileChunksWithManifestsBlockProvider(ILogger<ReconcileChunksWithManifestsBlockProvider> logger)
        {
            _logger = logger;
        }

        private readonly ILogger<ReconcileChunksWithManifestsBlockProvider> _logger;

        
        public ReconcileChunksWithManifestsBlockProvider AddChunksThatNeedToBeUploadedBeforeManifestCanBeCreated(Dictionary<BinaryFile, List<HashValue>> chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
        {
            _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;
            return this;
        }
        private Dictionary<BinaryFile, List<HashValue>> _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated;


        public TransformManyBlock<HashValue, BinaryFile> GetBlock()
        {
            return new(hashOfUploadedChunk => // IN: HashValue of Chunk , OUT: BinaryFiles for which to create Manifest
                {
                    try
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
                                    // Add it to the list of manifests to be created  //TODO WHY?!
                                    //kvp.Key.ManifestHash = kvp.Key.Hash;
                                    manifestsToCreate.Add(kvp.Key);
                                }
                            }

                            // Remove all reconciled manifests from the waitlist
                            foreach (var binaryFile in manifestsToCreate)
                                _chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.Remove(binaryFile);
                        }

                        return manifestsToCreate;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "ERRORTODO", hashOfUploadedChunk);
                        throw;
                    }
                });
        }
    }

    internal class CreateManifestBlockProvider
    {
        public CreateManifestBlockProvider(ILogger<CreateManifestBlockProvider> logger, AzureRepository azureRepository)
        {
            _logger = logger;
            _azureRepository = azureRepository;
        }

        private readonly ILogger<CreateManifestBlockProvider> _logger;
        private readonly AzureRepository _azureRepository;

        public TransformBlock<BinaryFile, object> GetBlock()
        {
            return new(async bf =>
            {
                try
                {
                    await _azureRepository.AddManifestAsync(bf, bf.Chunks.ToArray());

                    return bf.Hash;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO", bf);
                    throw;
                }
            });
        }
    }

    internal class CreatePointerBlockProvider
    {
        public CreatePointerBlockProvider(ILogger<CreatePointerBlockProvider> logger, PointerService ps)
        {
            _logger = logger;
            _ps = ps;
        }

        private readonly ILogger<CreatePointerBlockProvider> _logger;
        private readonly PointerService _ps;


        public CreatePointerBlockProvider AddBinaryFilesToDelete(List<BinaryFile> binaryFilesToDelete)
        {
            _binaryFilesToDelete = binaryFilesToDelete;
            return this;
        }
        private List<BinaryFile> _binaryFilesToDelete;


        public TransformBlock<BinaryFile, PointerFile> GetBlock()
        {
            return new(bf =>
            {
                try
                {
                    var p = _ps.CreatePointerFileIfNotExists(bf);
                    _binaryFilesToDelete.Add(bf);
                    return p;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO", bf);
                    throw;
                }
            });
        }
    }

    internal class CreatePointerFileEntryIfNotExistsBlockProvider
    {
        public CreatePointerFileEntryIfNotExistsBlockProvider(ILogger<CreatePointerFileEntryIfNotExistsBlockProvider> logger, AzureRepository azureRepository)
        {
            _logger = logger;
            _azureRepository = azureRepository;
        }

        private readonly ILogger<CreatePointerFileEntryIfNotExistsBlockProvider> _logger;
        private readonly AzureRepository _azureRepository;


        public CreatePointerFileEntryIfNotExistsBlockProvider AddVersion(DateTime version)
        {
            _version = version;
            return this;
        }
        private DateTime _version;


        public ActionBlock<PointerFile> GetBlock()
        {
            return new(async pointerFile =>
            {
                try
                {
                    await _azureRepository.CreatePointerFileEntryIfNotExistsAsync(pointerFile, _version);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO");
                    throw;
                }
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

        public RemoveDeletedPointersTaskProvider AddVersion(DateTime version)
        {
            _version = version;
            return this;
        }
        private DateTime _version;


        public Task GetTask()
        {
            return new(async () =>
            {
                try
                {
                    var pfes = await _azureRepository.GetCurrentEntriesAsync(true);
                    pfes = pfes.Where(e => e.Version < _version).ToList(); // that were not created in the current run (those are assumed to be up to date)

                    Parallel.ForEach(pfes, async pfe =>
                    {
                        var pointerFullName = Path.Combine(_root.FullName, pfe.RelativeName);
                        if (!File.Exists(pointerFullName) && !pfe.IsDeleted)
                            await _azureRepository.CreatePointerFileEntryIfNotExistsAsync(pfe, _version, true);
                    });
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO");
                    throw;
                }
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
        public DeleteBinaryFilesTaskProvider(ILogger<DeleteBinaryFilesTaskProvider> logger, ArchiveOptions options)
        {
            _logger = logger;
            _options = options;
        }

        private readonly ILogger<DeleteBinaryFilesTaskProvider> _logger;
        private readonly ArchiveOptions _options;

        public DeleteBinaryFilesTaskProvider AddBinaryFilesToDelete(List<BinaryFile> binaryFilesToDelete)
        {
            _binaryFilesToDelete = binaryFilesToDelete;
            return this;
        }
        private List<BinaryFile> _binaryFilesToDelete;


        public Task GetTask()
        {
            return new(() =>
            {
                try
                {
                    if (!_options.RemoveLocal)
                        return;

                    Parallel.ForEach(_binaryFilesToDelete, bf => bf.Delete());
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ERRORTODO");
                    throw;
                }
            });
        }
    }
}