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
using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;
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
            _azureRepository = ariusRepository;

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
        private readonly AzureRepository _azureRepository;


        public int Execute()
        {
            ////TODO Simulate
            ////TODO MINSIZE
            ////TODO CHeck if the archive is deduped and password by checking the first amnifest file


            var version = DateTime.Now;

            var fastHash = true;

            //Dowload db etc
            ManifestService.Init();

            var indexDirectoryBlock = new IndexDirectoryBlockProvider().GetBlock();


            var addHashBlock = new AddHashBlockProvider(_hvp, fastHash).GetBlock();


            var uploadedManifestHashes = new List<HashValue>(ManifestService.GetManifestHashes());
            var addRemoteManifestBlock = new AddRemoteManifestBlockProvider(uploadedManifestHashes).GetBlock();


            var chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = new Dictionary<HashValue, KeyValuePair<BinaryFile, List<HashValue>>>(); //Key = HashValue van de Manifest, List = HashValue van de Chunks
            var getChunksForUploadBlock = new GetChunksForUploadBlockProvider(_chunker, chunksThatNeedToBeUploadedBeforeManifestCanBeCreated, _azureRepository).GetBlock();

            
            var encryptChunksBlock = new EncryptChunksBlockProvider(_config, _encrypter).GetBlock();


            var uploadQueue = new BlockingCollection<EncryptedChunkFile>();
            var enqueueEncryptedChunksForUploadBlock = new EnqueueEncryptedChunksForUploadBlockProvider(uploadQueue).GetBlock();


            var uploadEncryptedChunksBlock = new UploadEncryptedChunksBlockProvider(_options, _azureRepository).GetBlock();


            var uploadTask = new UploadTaskProvider(uploadQueue, uploadEncryptedChunksBlock, enqueueEncryptedChunksForUploadBlock).GetTask();


            var reconcileChunksWithManifestsBlock = new ReconcileChunksWithManifestsBlockProvider(chunksThatNeedToBeUploadedBeforeManifestCanBeCreated).GetBlock();

            
            var createManifestBlock = new CreateManifestBlockProvider().GetBlock();


            var reconcileBinaryFilesWithManifestBlock = new ReconcileBinaryFilesWithManifestBlockProvider(uploadedManifestHashes).GetBlock();




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
                reconcileBinaryFilesWithManifestBlock,
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
                reconcileBinaryFilesWithManifestBlock,
                doNotPropagateCompletionOptions);


            // 180
            Task.WhenAll(createManifestBlock.Completion, addRemoteManifestBlock.Completion)
                .ContinueWith(_ => reconcileBinaryFilesWithManifestBlock.Complete());

            // 140
            reconcileBinaryFilesWithManifestBlock.LinkTo(
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

        


        

        

        
        

        
    }

    

    

    

    


    
}
