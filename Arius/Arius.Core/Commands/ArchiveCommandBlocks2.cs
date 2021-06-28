using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    internal class IndexBlock : TaskBlockBase<DirectoryInfo>
    {
        public IndexBlock(ILogger<IndexBlock> logger,
            DirectoryInfo root,
            Action<IFile> indexedFile,
            Action done)
            : base(logger: logger, source: root, done: done)
        {
            this.indexedFile = indexedFile;
        }

        private readonly Action<IFile> indexedFile;

        protected override Task TaskBodyImplAsync(DirectoryInfo root)
        {
            foreach (var file in IndexDirectory(root))
                indexedFile(file);

            return Task.CompletedTask;
        }


        private IEnumerable<IFile> IndexDirectory(DirectoryInfo directory) => IndexDirectory(directory, directory);
        private IEnumerable<IFile> IndexDirectory(DirectoryInfo root, DirectoryInfo directory)
        {
            foreach (var file in directory.GetFiles())
            {
                if (IsHiddenOrSystem(file))
                {
                    logger.LogDebug($"Skipping file {file.FullName} as it is SYSTEM or HIDDEN");
                    continue;
                }
                else if (IsIgnoreFile(file))
                {
                    logger.LogDebug($"Ignoring file {file.FullName}");
                    continue;
                }
                else
                {
                    yield return GetFile(root, file);
                }
            }

            foreach (var dir in directory.GetDirectories())
            {
                if (IsHiddenOrSystem(dir))
                {
                    logger.LogDebug($"Skipping directory {dir.FullName} as it is SYSTEM or HIDDEN");
                    continue;
                }

                foreach (var f in IndexDirectory(root, dir))
                    yield return f;
            }
        }
        private bool IsHiddenOrSystem(DirectoryInfo d)
        {
            if (d.Name == "@eaDir") //synology internals -- ignore
                return true;

            return IsHiddenOrSystem(d.Attributes);

        }
        private bool IsHiddenOrSystem(FileInfo fi)
        {
            if (fi.FullName.Contains("eaDir") ||
                fi.FullName.Contains("SynoResource"))
                //fi.FullName.Contains("@")) // commenting out -- email adresses are not weird
                logger.LogWarning("WEIRD FILE: " + fi.FullName);

            return IsHiddenOrSystem(fi.Attributes);
        }
        private static bool IsHiddenOrSystem(FileAttributes attr)
        {
            return (attr & FileAttributes.System) != 0 || (attr & FileAttributes.Hidden) != 0;
        }
        private static bool IsIgnoreFile(FileInfo fi)
        {
            var lowercaseFilename = fi.Name.ToLower();

            return lowercaseFilename.Equals("autorun.ini") ||
                lowercaseFilename.Equals("thumbs.db") ||
                lowercaseFilename.Equals(".ds_store");
        }
        private IFile GetFile(DirectoryInfo root, FileInfo fi)
        {
            RelativeFileBase file = fi.IsPointerFile() ?
                new PointerFile(root, fi) :
                new BinaryFile(root, fi);

            logger.LogInformation($"Found {file.GetType().Name} '{file.RelativeName}'");

            return file;
        }
    }


    internal class HashBlock : BlockingCollectionTaskBlockBase<IFile>
    {
        public HashBlock(ILogger<HashBlock> logger,
            BlockingCollection<IFile> source,
            int maxDegreeOfParallelism,
            Action<PointerFile> hashedPointerFile,
            Action<BinaryFile> hashedBinaryFile,
            IHashValueProvider hvp,
            Action done) : base(logger: logger, source: source, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.hashedPointerFile = hashedPointerFile;
            this.hashedBinaryFile = hashedBinaryFile;
            this.hvp = hvp;
        }

        private readonly Action<PointerFile> hashedPointerFile;
        private readonly Action<BinaryFile> hashedBinaryFile;
        private readonly IHashValueProvider hvp;

        protected override Task ForEachBodyImplAsync(IFile item)
        {
            if (item is PointerFile pf)
            {
                // A pointerfile already knows its hash
                hashedPointerFile(pf);
            }
            else if (item is BinaryFile bf)
            {
                logger.LogInformation($"Hashing BinaryFile '{bf.RelativeName}'...");

                // For BinaryFiles we need to calculate it
                bf.Hash = hvp.GetHashValue(bf);
                logger.LogInformation($"Hashing BinaryFile '{bf.RelativeName}'... done. Hash: '{bf.Hash.ToShortString()}'");
                hashedBinaryFile(bf);
            }
            else
                throw new ArgumentException($"Cannot add hash to item of type {item.GetType().Name}");

            return Task.CompletedTask;
        }
    }


    internal class ProcessHashedBinaryBlock : BlockingCollectionTaskBlockBase<BinaryFile>
    {
        public ProcessHashedBinaryBlock(ILogger<ProcessHashedBinaryBlock> logger,
           //Func<bool> continueWhile,
           BlockingCollection<BinaryFile> source,
           AzureRepository repo,
           Action<BinaryFile> binaryFileAlreadyBackedUp,
           Action<BinaryFile> uploadBinaryFile,
           Action<BinaryFile> waitForCreatedManifest,
           Action<BinaryFile> manifestExists,
           Action done) : base(logger: logger, source: source, done: done)
        {
            this.repo = repo;
            this.binaryFileAlreadyBackedUp = binaryFileAlreadyBackedUp;
            this.uploadBinaryFile = uploadBinaryFile;
            this.waitForCreatedManifest = waitForCreatedManifest;
            this.manifestExists = manifestExists;
        }

        private readonly AzureRepository repo;
        private readonly Action<BinaryFile> binaryFileAlreadyBackedUp;
        private readonly Action<BinaryFile> uploadBinaryFile;
        private readonly Action<BinaryFile> waitForCreatedManifest;
        private readonly Action<BinaryFile> manifestExists;

        protected override async Task ForEachBodyImplAsync(BinaryFile bf)
        {
            if (PointerService.GetPointerFile(bf) is var pf && pf is not null &&
                pf.Hash == bf.Hash)
            {
                //An equivalent PointerFile already exists and is already being sent through the pipe - skip.

                if (!await ManifestExists(bf.Hash))
                    throw new InvalidOperationException($"BinaryFile '{bf.Name}' has a PointerFile that points to a manifest ('{bf.Hash.ToShortString()}') that no longer exists.");

                logger.LogInformation($"BinaryFile '{bf.Name}' already has a PointerFile that is being processed. Skipping BinaryFile.");
                binaryFileAlreadyBackedUp(bf);

                return;
            }

            /* 
                * This BinaryFile does not yet have an equivalent PointerFile and may need to be uploaded.
                * Three possibilities:
                *   1. The manifest already exists (ie the binary is already uploaded but this may be a duplicate in another path) --> create the pointer
                *   2. The manifest does not exist and IS NOT yet being created --> upload the binary and send this BinaryFile to the waiting queue until it is uploaded
                *   3. The manifest does not exist and IS being created --> send this binaryFile to the waiting queue until it is uploaded
                */

            if (await ManifestExists(bf.Hash))
            {
                // 1 Exists remote
                logger.LogInformation($"Manifest for '{bf.Name}' ('{bf.Hash.ToShortString()}') already exists. No need to upload.");

                manifestExists(bf);

                return;
            }

            lock (creating)
            {
                if (!creating.Contains(bf.Hash))
                {
                    // 2 Does not yet exist remote and not yet being created --> upload
                    logger.LogInformation($"Manifest for '{bf.Name}' ('{bf.Hash.ToShortString()}') does not exist remotely. To upload and create pointer.");
                    creating.Add(bf.Hash);

                    uploadBinaryFile(bf);
                    waitForCreatedManifest(bf);

                    return;
                }
            }

            // 3 Does not exist remote but is being created
            logger.LogInformation($"Manifest for '{bf.Name}' ('{bf.Hash.ToShortString()}') does not exist remotely but is already being uploaded. To wait and create pointer.");

            waitForCreatedManifest(bf);
        }
        private readonly List<HashValue> creating = new();

        private Task<bool> ManifestExists(HashValue h)
        {
            //// Check cache
            //if (created.ContainsKey(h))
            //    return created[h];

            // Check remote
            var e = repo.ManifestExistsAsync(h); //TODO: Cache results - maar pas op met synchronization issues in CreateManifestBlock.manifestCreated handler
            //created.Add(h, e); //Add result to cache so we dont need to recheck again next time

            return e;
        }
        //private readonly Dictionary<HashValue, bool> created = new();
    }


    internal class ChunkBlock : BlockingCollectionTaskBlockBase<BinaryFile>
    {
        public ChunkBlock(ILogger<ChunkBlock> logger,
            BlockingCollection<BinaryFile> source,
            int maxDegreeOfParallelism,
            IChunker chunker,
            Action<BinaryFile, IChunkFile[]> chunkedBinary,
            Action done) : base(logger: logger, source: source, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.chunker = chunker;
            this.chunkedBinary = chunkedBinary;
        }

        private readonly IChunker chunker;
        private readonly Action<BinaryFile, IChunkFile[]> chunkedBinary;

        protected override Task ForEachBodyImplAsync(BinaryFile bf)
        {
            logger.LogInformation($"Chunking '{bf.Hash.ToShortString()}' ('{bf.RelativeName}')...");
            var chunks = chunker.Chunk(bf);
            logger.LogInformation($"Chunking '{bf.Hash.ToShortString()}' ('{bf.RelativeName}')... done. Created {chunks.Length} chunk(s)");
            logger.LogDebug($"Chunks for manifest '{bf.Hash.ToShortString()}': '{string.Join("', '", chunks.Select(c => c.Hash.ToShortString()))}'");

            chunkedBinary(bf, chunks);

            return Task.CompletedTask;
        }
    }


    internal class ProcessChunkBlock : BlockingCollectionTaskBlockBase<IChunkFile>
    {
        public ProcessChunkBlock(ILogger<ProcessChunkBlock> logger,
            BlockingCollection<IChunkFile> source,
            AzureRepository repo,
            Action<IChunkFile> chunkToUpload,
            Action<HashValue> chunkAlreadyUploaded,
            Action done) : base(logger: logger, source: source, done: done)
        {
            this.repo = repo;
            this.chunkToUpload = chunkToUpload;
            this.chunkAlreadyUploaded = chunkAlreadyUploaded;
        }

        private readonly AzureRepository repo;
        private readonly Action<IChunkFile> chunkToUpload;
        private readonly Action<HashValue> chunkAlreadyUploaded;

        protected override async Task ForEachBodyImplAsync(IChunkFile chunk)
        {
            if (await ChunkExists(chunk.Hash))
            {
                // 1 Exists remote
                logger.LogInformation($"Chunk with hash '{chunk.Hash.ToShortString()}' already exists. No need to upload.");

                if (chunk is ChunkFile)
                    chunk.Delete(); //The chunk is already uploaded, delete it. Do not delete a binaryfile at this stage.

                chunkAlreadyUploaded(chunk.Hash);

                return;
            }

            lock (creating)
            {
                if (!creating.Contains(chunk.Hash))
                {
                    // 2 Does not yet exist remote and not yet being created --> upload
                    logger.LogInformation($"Chunk with hash '{chunk.Hash.ToShortString()}' does not exist remotely. To upload.");
                    creating.Add(chunk.Hash);

                    chunkToUpload(chunk); //Signal that this chunk needs to be uploaded

                    return;
                }
            }

            // 3 Does not exist remote but is being created
            logger.LogInformation($"Chunk with hash '{chunk.Hash.ToShortString()}' does not exist remotely but is already being uploaded. To wait and create pointer.");
        }
        private readonly List<HashValue> creating = new();

        private async Task<bool> ChunkExists(HashValue h)
        {
            return await repo.ChunkExists(h); //TODO: CACHE RESULTS
        }
    }


    internal class EncryptChunkBlock : BlockingCollectionTaskBlockBase<IChunkFile>
    {
        public EncryptChunkBlock(ILogger<EncryptChunkBlock> logger,
            BlockingCollection<IChunkFile> source,
            int maxDegreeOfParallelism,
            TempDirectoryAppSettings tempDirAppSettings,
            IEncrypter encrypter,
            Action<EncryptedChunkFile> chunkEncrypted,
            Action done) : base(logger: logger, source: source, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.tempDirAppSettings = tempDirAppSettings;
            this.encrypter = encrypter;
            this.chunkEncrypted = chunkEncrypted;
        }

        private readonly TempDirectoryAppSettings tempDirAppSettings;
        private readonly IEncrypter encrypter;
        private readonly Action<EncryptedChunkFile> chunkEncrypted;

        protected override Task ForEachBodyImplAsync(IChunkFile chunkFile)
        {
            logger.LogInformation($"Encrypting chunk '{chunkFile.Hash.ToShortString()}' (source: '{chunkFile.Name}')");

            var targetFile = new FileInfo(Path.Combine(tempDirAppSettings.TempDirectoryFullName, "encryptedchunks", $"{chunkFile.Hash}{EncryptedChunkFile.Extension}"));

            encrypter.Encrypt(chunkFile, targetFile, SevenZipCommandlineEncrypter.Compression.NoCompression, chunkFile is not BinaryFile);

            var ecf = new EncryptedChunkFile(targetFile, chunkFile.Hash);

            logger.LogInformation($"Encrypting chunk '{chunkFile.Hash.ToShortString()}'... done");

            chunkEncrypted(ecf);

            return Task.CompletedTask;
        }
    }


    internal class CreateUploadBatchBlock : TaskBlockBase<BlockingCollection<EncryptedChunkFile>>
    {
        public CreateUploadBatchBlock(ILogger<CreateUploadBatchBlock> logger,
            BlockingCollection<EncryptedChunkFile> source,
            AzCopyAppSettings azCopyAppSettings,
            Func<bool> isAddingCompleted,
            Action<EncryptedChunkFile[]> batchForUpload,
            Action done) : base(logger: logger, source: source, done: done)
        {
            this.azCopyAppSettings = azCopyAppSettings;
            this.isAddingCompleted = isAddingCompleted;
            this.batchForUpload = batchForUpload;
        }

        private readonly AzCopyAppSettings azCopyAppSettings;
        private readonly Func<bool> isAddingCompleted;
        private readonly Action<EncryptedChunkFile[]> batchForUpload;

        protected override Task TaskBodyImplAsync(BlockingCollection<EncryptedChunkFile> source)
        {
            var uploadBatch = new List<EncryptedChunkFile>();

            while (!isAddingCompleted() //loop until the preceding block has finished adding chunks to be uploaded
                || !source.IsCompleted) //if adding is completed, it can be that we still need to generate multiple batches with whatever is left in the queue
            {
                string reason = "adding is completed"; //this is the default reason: if source is marked as CompleteAdding and the queue is empty, the for each loop will no longer loop
                long size = default;

                foreach (var item in source.GetConsumingEnumerable())
                {
                    uploadBatch.Add(item);
                    size += item.Length;

                    if (uploadBatch.Count >= azCopyAppSettings.BatchCount)
                    {
                        reason = "of batchcount";
                        break;
                    }
                    else if (size >= azCopyAppSettings.BatchSize)
                    {
                        reason = "of batchsize";
                        break;
                    }
                    else if (isAddingCompleted())
                    {
                        break;
                    }
                }

                if (uploadBatch.Any())
                {
                    logger.LogInformation($"Creating batch because {reason} with {uploadBatch.Count} element(s), total size: {size.GetBytesReadable()}. Remaining elements in queue: {source.Count}");
                    batchForUpload(uploadBatch.ToArray());
                    uploadBatch.Clear();
                }
            }

            return Task.CompletedTask;
        }
    }


    internal class UploadBatchBlock : BlockingCollectionTaskBlockBase<EncryptedChunkFile[]>
    {
        public UploadBatchBlock(ILogger<UploadBatchBlock> logger,
            BlockingCollection<EncryptedChunkFile[]> source,
            int maxDegreeOfParallelism,
            AzureRepository repo,
            AccessTier tier,
            Action<HashValue> chunkUploaded,
            Action done) : base(logger: logger, source: source, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.repo = repo;
            this.tier = tier;
            this.chunkUploaded = chunkUploaded;
        }

        private readonly AzureRepository repo;
        private readonly AccessTier tier;
        private readonly Action<HashValue> chunkUploaded;

        protected override Task ForEachBodyImplAsync(EncryptedChunkFile[] ecfs)
        {
            logger.LogInformation($"Uploading batch..."); // Remaining Batches queue depth: {_block!.Value.InputCount}");

            //Upload the files
            repo.Upload(ecfs, tier);

            //Delete the (temporary) encrypted chunk files
            foreach (var ecf in ecfs)
                ecf.Delete();

            logger.LogInformation($"Uploading batch... done");

            foreach (var chunk in ecfs)
                chunkUploaded(chunk.Hash);

            return Task.CompletedTask;
        }
    }


    internal class CreateManifestBlock : BlockingCollectionTaskBlockBase<(HashValue ManifestHash, HashValue[] ChunkHashes)>
    {
        public CreateManifestBlock(ILogger<CreateManifestBlock> logger,
            BlockingCollection<(HashValue ManifestHash, HashValue[] ChunkHashes)> source,
            int maxDegreeOfParallelism,
            AzureRepository repo,
            Action<HashValue> manifestCreated,
            Action done) : base(logger: logger, source: source, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.repo = repo;
            this.manifestCreated = manifestCreated;
        }

        private readonly AzureRepository repo;
        private readonly Action<HashValue> manifestCreated;

        protected override async Task ForEachBodyImplAsync((HashValue ManifestHash, HashValue[] ChunkHashes) item)
        {
            logger.LogInformation($"Creating manifest '{item.ManifestHash.ToShortString()}'...");

            await repo.AddManifestAsync(item.ManifestHash, item.ChunkHashes);
            manifestCreated(item.ManifestHash);

            logger.LogInformation($"Creating manifest '{item.ManifestHash.ToShortString()}'... done");
        }
    }


    internal class CreatePointerFileIfNotExistsBlock : BlockingCollectionTaskBlockBase<BinaryFile>
    {
        public CreatePointerFileIfNotExistsBlock(ILogger<CreatePointerFileIfNotExistsBlock> logger,
            BlockingCollection<BinaryFile> source,
            int maxDegreeOfParallelism,
            PointerService pointerService,
            Action<BinaryFile> succesfullyBackedUp,
            Action<PointerFile> pointerFileCreated,
            Action done) : base(logger: logger, source: source, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.pointerService = pointerService;
            this.succesfullyBackedUp = succesfullyBackedUp;
            this.pointerFileCreated = pointerFileCreated;
        }

        private readonly PointerService pointerService;
        private readonly Action<BinaryFile> succesfullyBackedUp;
        private readonly Action<PointerFile> pointerFileCreated;

        protected override Task ForEachBodyImplAsync(BinaryFile bf)
        {
            logger.LogInformation($"Creating pointer for '{bf.RelativeName}'...");

            var pf = pointerService.CreatePointerFileIfNotExists(bf);

            logger.LogInformation($"Creating pointer for '{bf.RelativeName}'... done");

            succesfullyBackedUp(bf);
            pointerFileCreated(pf);

            return Task.CompletedTask;
        }
    }


    internal class CreatePointerFileEntryIfNotExistsBlock : BlockingCollectionTaskBlockBase<PointerFile>
    {
        public CreatePointerFileEntryIfNotExistsBlock(ILogger<CreatePointerFileEntryIfNotExistsBlock> logger,
            BlockingCollection<PointerFile> source,
            int maxDegreeOfParallelism,
            AzureRepository repo,
            DateTime version,
            Action done) : base(logger: logger, source: source, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.repo = repo;
            this.version = version;
        }

        private readonly AzureRepository repo;
        private readonly DateTime version;

        protected override async Task ForEachBodyImplAsync(PointerFile pointerFile)
        {
            logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'...");

            var r = await repo.CreatePointerFileEntryIfNotExistsAsync(pointerFile, version);

            switch (r)
            {
                case AzureRepository.PointerFileEntryRepository.CreatePointerFileEntryResult.Upserted:
                    logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Upserted entry.");
                    break;
                case AzureRepository.PointerFileEntryRepository.CreatePointerFileEntryResult.InsertedDeleted:
                    logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Inserted 'deleted' entry.");
                    break;
                case AzureRepository.PointerFileEntryRepository.CreatePointerFileEntryResult.NoChange:
                    logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. No change made, latest entry was up to date.");
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
    }


    internal class DeleteBinaryFilesBlock : BlockingCollectionTaskBlockBase<BinaryFile>
    {
        public DeleteBinaryFilesBlock(ILogger<DeleteBinaryFilesBlock> logger,
            BlockingCollection<BinaryFile> source,
            int maxDegreeOfParallelism,
            bool removeLocal,
            Action done) : base(logger: logger, source: source, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.removeLocal = removeLocal;
        }

        private readonly bool removeLocal;

        protected override Task ForEachBodyImplAsync(BinaryFile bf)
        {
            if (removeLocal)
            {
                logger.LogInformation($"RemoveLocal flag is set - Deleting binary '{bf.RelativeName}'...");
                bf.Delete();
                logger.LogInformation($"RemoveLocal flag is set - Deleting binary '{bf.RelativeName}'... done");
            }

            return Task.CompletedTask;
        }
    }


    internal class CreateDeletedPointerFileEntryForDeletedPointerFilesBlock : BlockingCollectionTaskBlockBase<AzureRepository.PointerFileEntry>
    {
        public CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(ILogger<CreateDeletedPointerFileEntryForDeletedPointerFilesBlock> logger,
            BlockingCollection<AzureRepository.PointerFileEntry> source,
            int maxDegreeOfParallelism,
            AzureRepository repo,
            PointerService pointerService,
            DateTime version,
            Action start,
            Action done) : base(logger: logger, source: source, maxDegreeOfParallelism: maxDegreeOfParallelism, start: start, done: done)
        {
            this.repo = repo;
            this.pointerService = pointerService;
            this.version = version;
        }

        private readonly AzureRepository repo;
        private readonly PointerService pointerService;
        private readonly DateTime version;

        protected override async Task ForEachBodyImplAsync(AzureRepository.PointerFileEntry pfe)
        {
            if (!pfe.IsDeleted &&
                pointerService.GetPointerFile(pfe) is null && pointerService.GetBinaryFile(pfe) is null) //PointerFileEntry is marked as exists and there is no PointerFile and there is no BinaryFile (only on PointerFile may not work since it may still be in the pipeline to be created)
            {
                logger.LogInformation($"The pointer or binary for '{pfe.RelativeName}' no longer exists locally, marking entry as deleted");
                await repo.CreateDeletedPointerFileEntryAsync(pfe, version);
            }
        }
    }


    internal class ExportToJsonBlock : TaskBlockBase<BlockingCollection<AzureRepository.PointerFileEntry>>
    {
        public ExportToJsonBlock(ILogger<ExportToJsonBlock> logger,
            BlockingCollection<AzureRepository.PointerFileEntry> source,
            AzureRepository repo,
            DateTime version,
            Action start,
            Action done) : base(logger: logger, source: source, start: start, done: done) //! must be single threaded
        {
            this.repo = repo;
            //this.pointerService = pointerService;
            this.version = version;
        }

        private readonly AzureRepository repo;
        //private readonly PointerService pointerService;
        private readonly DateTime version;


        protected override Task TaskBodyImplAsync(BlockingCollection<AzureRepository.PointerFileEntry> source)
        {
            using Stream file = File.Create(@"c:\ha.json");

            var writer = new Utf8JsonWriter(file, new JsonWriterOptions() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping } );

            writer.WriteStartObject();

            writer.WriteStartArray("hehe");



            foreach (var entry in source.AsEnumerable()) //.GetConsumingEnumerable())
            {


                var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                //var x = JsonEncodedText.Encode(json, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
                writer.WriteStringValue(json);

                //writer.Flush();

            }

            writer.WriteEndArray();

            writer.WriteEndObject();






            writer.Flush();

            return Task.CompletedTask;
        }
    }
}
