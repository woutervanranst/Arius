﻿using System;
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

            // Dowload db etc
            ManifestService.Init();

            // Define blocks & intermediate variables
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


            var createPointersBlock = new CreatePointerBlockProvider().GetBlock();


            var updateManifestBlock = new UpdateManifestBlockProvider(_logger, version, _root).GetBlock();


            var removeDeletedPointersTask = new RemoveDeletedPointersTaskProvider(_logger, version, _root).GetTask();

            // Set up linking
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

            //addHashBlock.LinkTo(
            //    DataflowBlock.NullTarget<AriusArchiveItem>());


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


            // 115
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


            // 140
            Task.WhenAll(createManifestBlock.Completion, addRemoteManifestBlock.Completion)
                .ContinueWith(_ => reconcileBinaryFilesWithManifestBlock.Complete());


            // 150
            reconcileBinaryFilesWithManifestBlock.LinkTo(
                createPointersBlock,
                propagateCompletionOptions);


            // 160
            createPointersBlock.LinkTo(updateManifestBlock, 
                doNotPropagateCompletionOptions);


            // 170
            Task.WhenAll(createPointersBlock.Completion, addHashBlock.Completion)
                .ContinueWith(_ => updateManifestBlock.Complete());


            // 180
            updateManifestBlock.Completion
                .ContinueWith(_ => removeDeletedPointersTask);


            //Fill the flow
            indexDirectoryBlock.Post(_root);
            indexDirectoryBlock.Complete();

            
            // Wait for the end
            removeDeletedPointersTask.Wait();

            

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