using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml.Schema;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.CommandLine
{
    internal class ArchiveCommandExecutor : ICommandExecutor
    {
        public ArchiveCommandExecutor(ICommandExecutorOptions options,
            ILogger<ArchiveCommandExecutor> logger,

            IConfiguration config,
            AzureRepository ariusRepository,

            IHashValueProvider h,
            IChunker c,
            IEncrypter e)
        {
            _options = (ArchiveOptions)options;
            _logger = logger;
            _config = config;
            _root = new DirectoryInfo(_options.Path);
            _ariusRepository = ariusRepository;

            _hvp = h;
            _chunker = c;
            _encrypter = e;
        }

        private readonly ArchiveOptions _options;
        private readonly ILogger<ArchiveCommandExecutor> _logger;
        private readonly IConfiguration _config;

        private readonly DirectoryInfo _root;
        private readonly IHashValueProvider _hvp;
        private readonly IChunker _chunker;
        private readonly IEncrypter _encrypter;
        private readonly AzureRepository _ariusRepository;


        public int Execute()
        {
            ////TODO Simulate
            ////TODO MINSIZE
            ////TODO CHeck if the archive is deduped and password by checking the first amnifest file


            var version = DateTime.Now;

            var fastHash = true;

            //Dowload db etc
            using (var db = new Manifest())
            {
                db.Database.EnsureCreated();

                //var xxx = db.Manifests.Include(x => x.Chunks).SelectMany(x => x.Chunks).AsEnumerable().GroupBy(g => g.ChunkHashValue).Where(h => h.Count() > 1).ToList();

                //var yyy = xxx;
            }

            var indexDirectoryBlock = new TransformManyBlock<DirectoryInfo, IFile>(
                di => IndexDirectory(di),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 }
                );

            var addHashBlock = new TransformBlock<IFile, IFileWithHash>(
                file => (IFileWithHash)AddHash((dynamic) file, fastHash),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded }
            );

            //var castToBinaryBlock = new TransformBlock<IFileWithHash, BinaryFile>(item => (BinaryFile)item);
            var castToPointerBlock = new TransformBlock<IFileWithHash, PointerFile>(item => (PointerFile)item);

            //var processedOrProcessingBinaries = new List<HashValue>(); 
            //var allBinaryFiles = new ConcurrentDictionary<HashValue, BinaryFile>();

            var uploadedManifestHashes = new List<HashValue>();
            var uploadingManifestHashes = new List<HashValue>();

            using (var db = new Manifest())
            {
                uploadingManifestHashes.AddRange(db.Manifests.Select(m => new HashValue() {Value = m.HashValue}));
            }


            var manifestBeforePointers = new ConcurrentDictionary<HashValue, ConcurrentBag<BinaryFile>>(); //Key = HashValue van de Manifest
            var chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = new ConcurrentDictionary<HashValue, ConcurrentDictionary<HashValue, bool>>(); //Key = HashValue van de Manifest, ValueDict = HashValue van de Chunks

            var uploadedOrUploadingChunks = _ariusRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).ToList(); //a => a.Hash, a => a);
            
            var getChunksBlock = new TransformManyBlock<IFileWithHash, IChunkFile>(item =>
                {
                    var binaryFile = (BinaryFile) item;

                    Console.WriteLine("Chunking BinaryFile " + binaryFile.Name);

                    var addChunks = false;

                    // Check whether the binaryFile isn't already uploaded or in the course of being uploaded
                    lock (uploadedManifestHashes)
                    {
                        lock (uploadingManifestHashes)
                        {
                            var h = binaryFile.Hash;
                            if (!uploadedManifestHashes.Union(uploadingManifestHashes).Contains(h))
                            {
                                // Not yet uploaded or being uploaded --> add to the list of binary files that are being uploaded
                                uploadingManifestHashes.Add(h);
                                addChunks = true;
                            }
                        }
                    }

                    // Add this binaryFile to the list of pointers to be created, once this manifest is created
                    var bag = manifestBeforePointers.GetOrAdd(binaryFile.Hash, new ConcurrentBag<BinaryFile>());
                    bag.Add(binaryFile);

                    if (addChunks)
                    {
                        var toUpload = new List<IChunkFile>();

                        // Process the chunks
                        var chunks = AddChunks(binaryFile).Where(chunk =>
                             {
                                 var h = chunk.Hash;

                                 lock (uploadedOrUploadingChunks)
                                 {
                                     if (uploadedOrUploadingChunks.Contains(h))
                                     {
                                         chunk.Delete();
                                         return false;
                                     }

                                     uploadedOrUploadingChunks.Add(h);
                                     toUpload.Add(chunk);
                                 }

                                 ConcurrentDictionary<HashValue, bool> GetNewDictionary(HashValue hash)
                                 {
                                     var newDict = new ConcurrentDictionary<HashValue, bool>();
                                     newDict.TryAdd(hash, false);

                                     return newDict;
                                 }

                                 //ConcurrentDictionary<HashValue, bool> UpdateDictionary(HashValue _, ConcurrentDictionary<HashValue, bool> xx)
                                 //{
                                 //    var x = xx;
                                 //    x.TryAdd(chunk.Hash, false);

                                 //    return x;
                                 //}

                                 chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.AddOrUpdate(binaryFile.Hash,
                                     GetNewDictionary(chunk.Hash),
                                     (a, dict) =>
                                     {
                                         var x = chunksThatNeedToBeUploadedBeforeManifestCanBeCreated[binaryFile.Hash];
                                         x.TryAdd(chunk.Hash, false);

                                         return x;
                                     }
                                 );
 
                                return true;
                             });


                        //Console.WriteLine("Chunking BinaryFile " + binaryFile.Name + "... done & chunked");

                        return chunks;
                    }
                    else
                    {
                        Console.WriteLine("Chunking BinaryFile " + binaryFile.Name + "... done (not chunked - already done)");

                        return Enumerable.Empty<ChunkFile>();
                    }
                },
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded}
            );


            var encryptChunksBlock = new TransformBlock<IChunkFile, EncryptedChunkFile>(
                chunkFile => Encrypt(chunkFile),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 8 });

            
            var uploadQueue = new BlockingCollection<EncryptedChunkFile>();
            var enqueueEncryptedChunksForUploadBlock = new ActionBlock<EncryptedChunkFile>(item => uploadQueue.Add(item));


            const int AzCopyBatchSize = 256 * 1024 * 1024; //256 MB
            const int AzCopyBatchCount = 128;
            Task GetUploadTask(ITargetBlock<EncryptedChunkFile[]> b)
            {
                return Task.Run(() =>
                {
                    Thread.CurrentThread.Name = "Uploader";

                    while (!encryptChunksBlock.Completion.IsCompleted || 
                           //encryptChunksBlock.OutputCount > 0 || 
                           uploadQueue.Count > 0)
                    {
                            var uploadBatch = new List<EncryptedChunkFile>();
                            long size = 0;
                            foreach (var ecf in uploadQueue.GetConsumingEnumerable())
                            {
                                uploadBatch.Add(ecf);
                                size += ecf.Length;
                            }

                            if (size >= AzCopyBatchSize || 
                                uploadBatch.Count >= AzCopyBatchCount ||
                                uploadQueue.IsCompleted)    //if we re at the end of the queue, upload the remainder
                            {
                                b.Post(uploadBatch.ToArray());
                                break;
                            }
                    }
                });
            }


            var uploadEncryptedChunksBlock = new TransformManyBlock<EncryptedChunkFile[], RemoteEncryptedChunkBlobItem2>(
                encryptedChunkFiles =>
                {
                    //Upload the files
                    var uploadedBlobs = _ariusRepository.Upload(encryptedChunkFiles, _options.Tier);

                    //Delete the files
                    foreach (var encryptedChunkFile in encryptedChunkFiles)
                        encryptedChunkFile.Delete();

                    //foreach (var uploadedBlob in uploadedBlobs)  //TODO dit duurt mega lang / concurrent dictionary of nieuwe select?
                    //    uploadedOrUploadingChunks[uploadedBlob.Hash] = uploadedBlob;

                    return uploadedBlobs;
                },
                new ExecutionDataflowBlockOptions() {  MaxDegreeOfParallelism = 2 });


            var reconcileChunksWithManifestBlock = new TransformManyBlock<RemoteEncryptedChunkBlobItem2, HashValue>(
                recbi =>
                {
                    var hashOfUploadedChunk = recbi.Hash;

                    var manifestToCreate = new List<HashValue>();

                    foreach (var manifestHashValueWithChunkList in chunksThatNeedToBeUploadedBeforeManifestCanBeCreated) //pas op,hier met "de collection was modified"
                    {
                        if (manifestHashValueWithChunkList.Value.TryUpdate(hashOfUploadedChunk, true, false))
                        {
                            //A value was updated
                            if (manifestHashValueWithChunkList.Value.All(c => c.Value))
                            {
                                //All chunks for this manifest are now uploaded

                                //Remove this mnaifest from the pendingg list
                                if (!chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.TryRemove(manifestHashValueWithChunkList.Key, out _))
                                    throw new InvalidOperationException();

                                //Add this manifest to the "to create" list
                                manifestToCreate.Add(manifestHashValueWithChunkList.Key);
                            }
                        }
                    }

                    return manifestToCreate;
                });


            var createManifestBlock = new TransformManyBlock<HashValue, BinaryFile>(manifestHash =>
            {
                //Get & remove
                if (!manifestBeforePointers.TryRemove(manifestHash, out var binaryFilesBag))
                    throw new InvalidOperationException();

                var binaryFiles = binaryFilesBag.ToArray();
                var chunkHashValues = binaryFiles
                    .Single(bf => bf.Chunks != null && bf.Chunks.Any()) //Only one fo the binaryFiles will have the chunks (the other BinaryFiles have the same set of chunks)
                    .Chunks
                    .Select(c => c.Hash!.Value).ToArray();

                // Create the manifest
                using (var db = new Manifest())
                {
                    db.Manifests.Add(new ManifestEntry
                    {
                        HashValue = manifestHash.Value,
                        Chunks = chunkHashValues.Select((hv, i) => new OrderedChunk
                        {
                            ManifestHashValue = manifestHash.Value,
                            ChunkHashValue = hv, 
                            Order = i
                        }).ToList()
                    });
                    db.SaveChanges();
                }

                // Return the items that need to be added to the manifest
                return binaryFiles;
            });

            var createPointersBlock = new TransformBlock<IFileWithHash, PointerFile>(item =>
            {
                var binaryFile = (BinaryFile)item;
                var p = binaryFile.CreatePointerFile();

                return p;
            });

            var updateManifestBlock = new ActionBlock<PointerFile>(pointerFile =>
            {
                // Update the manifest
                using (var db = new Manifest())
                {
                    var me = db.Manifests
                        .Include(me => me.Entries)
                        .Single(m => m.HashValue == pointerFile.Hash!.Value);

                    //TODO iets met PointerFileEntryEqualityComparer?

                    var e = new PointerFileEntry
                    {
                        RelativeName = Path.GetRelativePath(_root.FullName, pointerFile.FullName),
                        Version = version,
                        CreationTimeUtc = File.GetCreationTimeUtc(pointerFile.FullName), //TODO
                        LastWriteTimeUtc = File.GetLastWriteTimeUtc(pointerFile.FullName),
                        IsDeleted = false
                    };

                    var xx = new PointerFileEntryEqualityComparer();
                    if (!me.Entries.Contains(e, xx))
                        me.Entries.Add(e);

                    _logger.LogInformation($"Added {e.RelativeName}");

                    db.SaveChanges();
                }

                //return item;
            });


            //var endBlock = new ActionBlock<AriusArchiveItem>(item => Console.WriteLine("done"));


            indexDirectoryBlock.LinkTo(
                addHashBlock,
                new DataflowLinkOptions { PropagateCompletion = true });


            addHashBlock.LinkTo(
                getChunksBlock,
                new DataflowLinkOptions { PropagateCompletion = false }, // DO NOT PROPAGATE
                x => x is BinaryFile);

            addHashBlock.LinkTo(
                createPointersBlock,
                new DataflowLinkOptions() { PropagateCompletion = false },
                x => x is BinaryFile);

            addHashBlock.LinkTo(
                castToPointerBlock,
                new DataflowLinkOptions { PropagateCompletion = true },
                x => x is PointerFile);

            castToPointerBlock.LinkTo(updateManifestBlock
                // DO NOT PROPAGATE COMPLETION HERE
            );

            //addHashBlock.LinkTo(
            //    DataflowBlock.NullTarget<AriusArchiveItem>());


            getChunksBlock.LinkTo(
                encryptChunksBlock,
                new DataflowLinkOptions { PropagateCompletion = true });


            encryptChunksBlock.LinkTo(
                enqueueEncryptedChunksForUploadBlock,
                new DataflowLinkOptions {PropagateCompletion = true});

            Task.WhenAll(enqueueEncryptedChunksForUploadBlock.Completion)
                .ContinueWith(_ => uploadQueue.CompleteAdding());

            var uploadTask = GetUploadTask((ITargetBlock<EncryptedChunkFile[]>)uploadEncryptedChunksBlock);

            Task.WhenAll(uploadTask)
                .ContinueWith(_ =>
                {
                    uploadEncryptedChunksBlock.Complete();
                });

            uploadEncryptedChunksBlock.LinkTo(
                reconcileChunksWithManifestBlock,
                new DataflowLinkOptions() {PropagateCompletion = true});

            reconcileChunksWithManifestBlock.LinkTo(
                createManifestBlock,
                new DataflowLinkOptions() {PropagateCompletion = true});

            createManifestBlock.LinkTo(
                createPointersBlock,
                new DataflowLinkOptions() {PropagateCompletion = true}
                );

            createPointersBlock.LinkTo(
                updateManifestBlock
                // DO NOT PROPAGATE
            );


            indexDirectoryBlock.Post(_root);
            indexDirectoryBlock.Complete();

            

            Task.WhenAll(createManifestBlock.Completion, castToPointerBlock.Completion)
                .ContinueWith(_ => updateManifestBlock.Complete());

            updateManifestBlock.Completion.Wait();

            using (var db = new Manifest())
            {
                //Not parallel foreach since DbContext is not thread safe

                foreach (var m in db.Manifests.Include(m => m.Entries))
                {
                    foreach (var e in m.GetLastEntries(false).Where(e => e.Version != version))
                    {
                        //TODO iets met PointerFileEntryEqualityComparer?

                        var p = Path.Combine(_root.FullName, e.RelativeName);
                        if (!File.Exists(p))
                        {
                            m.Entries.Add(new PointerFileEntry(){
                                RelativeName = e.RelativeName,
                                Version = version,
                                IsDeleted = true,
                                CreationTimeUtc = null,
                                LastWriteTimeUtc = null
                            });

                            _logger.LogInformation($"Marked {e.RelativeName} as deleted");
                        }
                    }
                }
            }

            using (var db = new Manifest())
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

                using (Stream file = File.Create(@"c:\ha.json"))
                {
                    JsonSerializer.SerializeAsync(file, db.Manifests
                            .Include(a => a.Chunks)
                            .Include(a => a.Entries),
                        new JsonSerializerOptions {WriteIndented = true});
                }
            }

            return 0;
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

        public IEnumerable<IChunkFile> AddChunks(BinaryFile f)
        {
            Console.WriteLine("Chunking BinaryFile " + f.Name);

            var cs = _chunker.Chunk(f);
            f.Chunks = cs;

            Console.WriteLine("Chunking BinaryFile " + f.Name + " done");

            return cs;
        }

        private EncryptedChunkFile Encrypt(IChunkFile f)
        {
            Console.WriteLine("Encrypting ChunkFile2 " + f.Name);

            var targetFile = new FileInfo(Path.Combine(_config.TempDir.FullName, "encryptedchunks", $"{f.Hash}{EncryptedChunkFile.Extension}"));

            _encrypter.Encrypt(f, targetFile, SevenZipCommandlineEncrypter.Compression.NoCompression, f is not BinaryFile);

            var ecf = new EncryptedChunkFile(targetFile, f.Hash);

            Console.WriteLine("Encrypting ChunkFile2 " + f.Name + " done");

            return ecf;
        }
    }

    

    

    

    


    
}
