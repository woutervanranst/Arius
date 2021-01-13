using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
            ManifestService.Init();

            var indexDirectoryBlock = new TransformManyBlock<DirectoryInfo, IFile>(
                di => IndexDirectory(di),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 }
                );




            var addHashBlock = new TransformBlock<IFile, IFileWithHash>(
                file => (IFileWithHash)AddHash((dynamic) file, fastHash),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded }
            );




            var uploadedManifestHashes = new List<HashValue>(ManifestService.GetManifestHashes());
            var addRemoteManifestBlock = new TransformBlock<IFileWithHash, BinaryFile>(item =>
            {
                var binaryFile = (BinaryFile)item;

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
            });




            var chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = new Dictionary<HashValue, KeyValuePair<BinaryFile, List<HashValue>>>(); //Key = HashValue van de Manifest, List = HashValue van de Chunks
            var uploadedOrUploadingChunks = _ariusRepository.GetAllChunkBlobItems().Select(recbi => recbi.Hash).ToList(); //a => a.Hash, a => a);
            var getChunksForUploadBlock = new TransformManyBlock<BinaryFile, IChunkFile>(binaryFile =>
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
                },
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded }
            );



            var encryptChunksBlock = new TransformBlock<IChunkFile, EncryptedChunkFile>(
                chunkFile =>
                {
                    return Encrypt(chunkFile);
                },
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



            
            var uploadEncryptedChunksBlock = new TransformManyBlock<EncryptedChunkFile[], HashValue>(
                encryptedChunkFiles =>
                {
                    //Upload the files
                    var uploadedBlobs = _ariusRepository.Upload(encryptedChunkFiles, _options.Tier);

                    //Delete the files
                    foreach (var encryptedChunkFile in encryptedChunkFiles)
                        encryptedChunkFile.Delete();

                    return uploadedBlobs.Select(recbi => recbi.Hash);
                },
                new ExecutionDataflowBlockOptions() {  MaxDegreeOfParallelism = 2 });


            
            
            var reconcileChunksWithManifestsBlock = new TransformManyBlock<HashValue, BinaryFile>(    // IN: HashValue of Chunk , OUT: BinaryFiles for which to create Manifest
                hashOfUploadedChunk =>
                {
                    var manifestsToCreate = new List<BinaryFile>();

                    lock (chunksThatNeedToBeUploadedBeforeManifestCanBeCreated)
                    {
                        foreach (var kvp in chunksThatNeedToBeUploadedBeforeManifestCanBeCreated) //Key = HashValue van de Manifest, List = HashValue van de Chunks
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
                            chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.Remove(manifestHash.Hash);
                    }

                    return manifestsToCreate;
                });



            
            var createManifestBlock = new TransformBlock<BinaryFile, BinaryFile>(binaryFile =>
            {
                var me = ManifestService.AddManifest(binaryFile);

                return binaryFile;
            });




            var binaryFilesPerManifestHash = new Dictionary<HashValue, List<BinaryFile>>(); //Key = HashValue van de Manifest
            var reconcileBinaryFilesWithManifest = new TransformManyBlock<BinaryFile, BinaryFile>(binaryFile =>
            {
                lock (binaryFilesPerManifestHash)
                {
                    //Add to the list an wait until EXACTLY ONE binaryFile with the 
                    if (!binaryFilesPerManifestHash.ContainsKey(binaryFile.Hash))
                        binaryFilesPerManifestHash.Add(binaryFile.Hash, new List<BinaryFile>());

                    // Add this binaryFile to the list of pointers to be created, once this manifest is created
                    binaryFilesPerManifestHash[binaryFile.Hash].Add(binaryFile);

                    if (binaryFile.ManifestHash.HasValue)
                    {
                        lock (uploadedManifestHashes)
                        {
                            uploadedManifestHashes.Add(binaryFile.ManifestHash.Value);
                        }
                    }

                    if (uploadedManifestHashes.Contains(binaryFile.Hash))
                    {
                        var pointersToCreate = binaryFilesPerManifestHash[binaryFile.Hash].ToArray();
                        binaryFilesPerManifestHash[binaryFile.Hash].Clear();

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




            var createPointersBlock = new TransformBlock<BinaryFile, PointerFile>(binaryFile =>
            {
                var p = binaryFile.EnsurePointerExists();

                return p;
            });




            var updateManifestBlock = new ActionBlock<PointerFile>(pointerFile =>
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
            });


            var propagateCompletionOptions = new DataflowLinkOptions() {PropagateCompletion = true};
            var doNotPropagateCompletionOptions = new DataflowLinkOptions() {PropagateCompletion = false};

            // 10
            indexDirectoryBlock.LinkTo(
                addHashBlock,
                propagateCompletionOptions);


            // 20
            addHashBlock.LinkTo(
                addRemoteManifestBlock,
                propagateCompletionOptions,
                x => x is BinaryFile);

            // 30
            addHashBlock.LinkTo(
                updateManifestBlock,
                doNotPropagateCompletionOptions,
                x => x is PointerFile,
                f => (PointerFile)f);


            // 40
            addRemoteManifestBlock.LinkTo(
                getChunksForUploadBlock,
                propagateCompletionOptions, 
                binaryFile => !binaryFile.ManifestHash.HasValue);

            // 50
            addRemoteManifestBlock.LinkTo(
                reconcileBinaryFilesWithManifest,
                doNotPropagateCompletionOptions,
                binaryFile => binaryFile.ManifestHash.HasValue);



            // 60
            getChunksForUploadBlock.LinkTo(
                encryptChunksBlock, 
                propagateCompletionOptions, 
                f => !f.Uploaded);

            // 70
            getChunksForUploadBlock.LinkTo(
                reconcileChunksWithManifestsBlock,
                doNotPropagateCompletionOptions,
                f => f.Uploaded,
                cf => cf.Hash);


            

            //addHashBlock.LinkTo(
            //    DataflowBlock.NullTarget<AriusArchiveItem>());



            // 80
            encryptChunksBlock.LinkTo(
                enqueueEncryptedChunksForUploadBlock,
                propagateCompletionOptions);



            // 90
            Task.WhenAll(enqueueEncryptedChunksForUploadBlock.Completion)
                .ContinueWith(_ => uploadQueue.CompleteAdding());

            var uploadTask = GetUploadTask(uploadEncryptedChunksBlock);

            // 100
            Task.WhenAll(uploadTask)
                .ContinueWith(_ => uploadEncryptedChunksBlock.Complete());

            
            
            // 110
            uploadEncryptedChunksBlock.LinkTo(
                reconcileChunksWithManifestsBlock,
                doNotPropagateCompletionOptions);

            // 190
            Task.WhenAll(uploadEncryptedChunksBlock.Completion, getChunksForUploadBlock.Completion)
                .ContinueWith(_ => reconcileChunksWithManifestsBlock.Complete());


            // 120
            reconcileChunksWithManifestsBlock.LinkTo(
                createManifestBlock,
                propagateCompletionOptions);

            
            
            // 130
            createManifestBlock.LinkTo(
                reconcileBinaryFilesWithManifest,
                doNotPropagateCompletionOptions);


            // 180
            Task.WhenAll(createManifestBlock.Completion, addRemoteManifestBlock.Completion)
                .ContinueWith(_ => reconcileBinaryFilesWithManifest.Complete());

            // 140
            reconcileBinaryFilesWithManifest.LinkTo(
                createPointersBlock,
                propagateCompletionOptions);

            // 150
            createPointersBlock.LinkTo(updateManifestBlock, 
                doNotPropagateCompletionOptions);

            // 170
            Task.WhenAll(createPointersBlock.Completion, addHashBlock.Completion)
                .ContinueWith(_ => updateManifestBlock.Complete());



            
            indexDirectoryBlock.Post(_root);
            indexDirectoryBlock.Complete();

            

            
            updateManifestBlock.Completion.Wait();

            using (var db = new ManifestStore())
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

                db.SaveChanges();
            }

            using (var db = new ManifestStore())
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

            var cs = _chunker.Chunk(f).ToArray();
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
