using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.HighPerformance;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Commands.Archive
{
    internal class IndexBlock : TaskBlockBase<DirectoryInfo>
    {
        public IndexBlock(ILogger<IndexBlock> logger,
            Func<DirectoryInfo> sourceFunc,
            int maxDegreeOfParallelism,
            bool fastHash,
            PointerService pointerService,
            Repository repo,
            Action<PointerFile> indexedPointerFile,
            Action<(BinaryFile BinaryFile, bool AlreadyBackedUp)> indexedBinaryFile,
            IHashValueProvider hvp,
            Action done)
            : base(logger: logger, sourceFunc: sourceFunc, done: done)
        {
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.fastHash = fastHash;
            this.pointerService = pointerService;
            this.repo = repo;
            this.indexedPointerFile = indexedPointerFile;
            this.indexedBinaryFile = indexedBinaryFile;
            this.hvp = hvp;
        }

        private readonly int maxDegreeOfParallelism;
        private readonly bool fastHash;
        private readonly PointerService pointerService;
        private readonly Repository repo;
        private readonly Action<PointerFile> indexedPointerFile;
        private readonly Action<(BinaryFile BinaryFile, bool AlreadyBackedUp)> indexedBinaryFile;
        private readonly IHashValueProvider hvp;

        protected override async Task TaskBodyImplAsync(DirectoryInfo root)
        {
            foreach (var fi in root.GetAllFileInfos(logger)
                                    .AsParallel()
                                    .WithDegreeOfParallelism(maxDegreeOfParallelism))
            {
                var rn = fi.GetRelativeName(root);

                if (fi.IsPointerFile())
                {
                    //PointerFile
                    logger.LogInformation($"Found PointerFile '{rn}'");

                    var pf = new PointerFile(root, fi);

                    indexedPointerFile(pf);
                }
                else
                {
                    //BinaryFile
                    logger.LogInformation($"Found BinaryFile '{rn}'");

                    //Get the Hash for this file
                    ManifestHash manifestHash;
                    var pf = pointerService.GetPointerFile(root, fi);
                    if (fastHash && pf is not null)
                    {
                        //A corresponding PointerFile exists
                        logger.LogDebug($"Using fasthash for '{rn}'");
                        manifestHash = pf.Hash;
                    }
                    else
                    { 
                        manifestHash = hvp.GetManifestHash(fi);
                    }

                    logger.LogInformation($"Hashing BinaryFile '{rn}'... done. Hash: '{manifestHash.ToShortString()}'");


                    var bf = new BinaryFile(root, fi, manifestHash);
                    

                    if (pf is not null && pf.Hash == manifestHash)
                    {
                        //An equivalent PointerFile already exists and is already being sent through the pipe - skip.

                        if (!await repo.ManifestExistsAsync(manifestHash))
                            throw new InvalidOperationException($"BinaryFile '{bf.RelativeName}' has a PointerFile that points to a manifest ('{manifestHash.ToShortString()}') that no longer exists.");

                        logger.LogInformation($"BinaryFile '{bf.RelativeName}' already has a PointerFile that is being processed. Skipping BinaryFile.");
                        indexedBinaryFile((bf, AlreadyBackedUp: true));
                    }
                    else
                    {
                        // To process
                        indexedBinaryFile((bf, AlreadyBackedUp: false));
                    }
                }
            }
        }
    }


    internal class ProcessHashedBinaryBlock : BlockingCollectionTaskBlockBase<BinaryFile>
    {
        public ProcessHashedBinaryBlock(ILogger<ProcessHashedBinaryBlock> logger,
           Func<BlockingCollection<BinaryFile>> sourceFunc,
           Repository repo,
           Action<BinaryFile> uploadBinaryFile,
           Action<BinaryFile> waitForCreatedManifest,
           Action<BinaryFile> manifestExists,
           Action done) : base(logger: logger, sourceFunc: sourceFunc, done: done)
        {
            this.repo = repo;
            this.uploadBinaryFile = uploadBinaryFile;
            this.waitForCreatedManifest = waitForCreatedManifest;
            this.manifestExists = manifestExists;
        }

        private readonly Repository repo;
        private readonly Action<BinaryFile> uploadBinaryFile;
        private readonly Action<BinaryFile> waitForCreatedManifest;
        private readonly Action<BinaryFile> manifestExists;

        protected override async Task ForEachBodyImplAsync(BinaryFile bf)
        {
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
        private readonly List<ManifestHash> creating = new();

        private Task<bool> ManifestExists(ManifestHash h)
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


    internal class NewBlock : BlockingCollectionTaskBlockBase<BinaryFile>
    {
        private readonly Chunker chunker;
        private readonly IHashValueProvider hvp;
        private readonly Repository repo;
        private readonly IBlobCopier.IOptions options;

        public NewBlock(ILogger<NewBlock> logger,
           Func<BlockingCollection<BinaryFile>> sourceFunc,
           Chunker chunker,
           IHashValueProvider hvp,
           Repository repo,
           IBlobCopier.IOptions options,
           //Action<BinaryFile> uploadBinaryFile,
           //Action<BinaryFile> waitForCreatedManifest,
           //Action<BinaryFile> manifestExists,
           Action done) : base(logger: logger, sourceFunc: sourceFunc, done: done)
        {
            this.chunker = chunker;
            this.hvp = hvp;
            this.repo = repo;
            this.options = options;
        }
        protected override async Task ForEachBodyImplAsync(BinaryFile bf)
        {
            var f = @"E:\SERIES_TODO\Friends\Friends.S01.720p.BluRay.x264-PSYCHD[rartv]\Friends.S01.720p.BluRay.x264-PSYCHD[rartv].7z";
            using var fs = File.OpenRead(/*bf.FullName*/f);
            var u = new Uploader(options);

            Stopwatch x = new();
            x.Start();

            using (var s = File.OpenRead(f))
            {
                await u.UploadChunkAsync(s, new("ehehe"));
            }


            //foreach (var chunk in ((ByteBoundaryChunker)chunker).Chunk(fs).AsParallel().WithDegreeOfParallelism(8))
            //{
            //    var ch = hvp.GetChunkHash(chunk.AsStream());

            //    if (await repo.ChunkExists(ch))
            //        continue;

            //    var compressed = Compress(chunk, CompressionLevel.Optimal);
            //    var ratio = compressed.Length / (double)chunk.Length;

            //    //var key = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes("haha"));
            //    var encrypted = await EncryptAsync(compressed, "woutervr");


            //    var decrypted = await DecryptAsync(encrypted, "woutervr");
            //    var decompressed = Decompress(decrypted);

            //    await u.UploadChunkAsync(encrypted, ch);
            //}

            x.Stop();

            //var Mbps = (new FileInfo(f)).Length * 8 / (1024 * 1024 * (double)x.ElapsedMilliseconds / 1000);
        }


        public static ReadOnlyMemory<byte> Compress(ReadOnlyMemory<byte> decompressed, CompressionLevel compressionLevel = CompressionLevel.Fastest) //https://stackoverflow.com/a/39157149/1582323
        {
            var compressed = new MemoryStream();
            using (var zip = new GZipStream(compressed, compressionLevel, true))
            {
                decompressed.AsStream().CopyTo(zip);
            }

            compressed.Seek(0, SeekOrigin.Begin);
            return compressed.ToArray().AsMemory();
        }

        public static ReadOnlyMemory<byte> Decompress(ReadOnlyMemory<byte> compressed)
        {
            var decompressed = new MemoryStream();
            using (var zip = new GZipStream(compressed.AsStream(), CompressionMode.Decompress, true))
            {
                zip.CopyTo(decompressed);
            }

            decompressed.Seek(0, SeekOrigin.Begin);
            return decompressed.ToArray().AsMemory();
        }



        // https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes?view=net-5.0
        // https://stackoverflow.com/questions/37689233/encrypt-decrypt-stream-in-c-sharp-using-rijndaelmanaged
        // https://asecuritysite.com/encryption/open_aes?val1=hello&val2=qwerty&val3=241fa86763b85341

        static async Task<ReadOnlyMemory<byte>> EncryptAsync(ReadOnlyMemory<byte> plain, string password)
        {
            DeriveBytes(password, out var key, out var iv);

            using var aesAlg = Aes.Create();
            aesAlg.Key = key;
            aesAlg.IV = iv;

            using var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            using var to = new MemoryStream();
            using var writer = new CryptoStream(to, encryptor, CryptoStreamMode.Write);

            await writer.WriteAsync(plain);
            writer.FlushFinalBlock();

            return to.ToArray().AsMemory();
        }

        static async Task<ReadOnlyMemory<byte>> DecryptAsync(ReadOnlyMemory<byte> cipher, string password)
        {
            DeriveBytes(password, out var key, out var iv);

            using var aesAlg = Aes.Create();
            aesAlg.Key = key;
            aesAlg.IV = iv;

            using var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

            using var to = new MemoryStream();
            using var writer = new CryptoStream(to, decryptor, CryptoStreamMode.Write);

            await writer.WriteAsync(cipher);
            writer.FlushFinalBlock();

            return to.ToArray().AsMemory();
        }


        //private static void DeriveBytes(string password, Hash salt, out byte[] key, out byte[] iv)
        //{
        //    //https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes?view=net-5.0

        //    //var salt = new byte[8];
        //    //using var rngCsp = new RNGCryptoServiceProvider();
        //    //rngCsp.GetBytes(salt);

        //    using var derivedBytes = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes(salt.Value));
        //    key = derivedBytes.GetBytes(32);
        //    iv = derivedBytes.GetBytes(16);
        //}

        private static void DeriveBytes(string password, out byte[] key, out byte[] iv)
        {
            //https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes?view=net-5.0

            //var salt = new byte[8];
            //using var rngCsp = new RNGCryptoServiceProvider();
            //rngCsp.GetBytes(salt);

            var salt = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(password)); //NOTE for eternity: GuillaumeB sait it will be ok

            using var derivedBytes = new Rfc2898DeriveBytes(password, salt);
            key = derivedBytes.GetBytes(32);
            iv = derivedBytes.GetBytes(16);
        }




        class Uploader
        {
            private readonly IBlobCopier.IOptions options;

            public Uploader(IBlobCopier.IOptions options)
            {
                this.options = options;

                //var bsc = new BlobServiceClient(connectionString);
                //container = bsc.GetBlobContainerClient(options.Container);

                //var r = container.CreateIfNotExists(PublicAccessType.None);

                //if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
                //this.logger.LogInformation($"Created container {options.Container}... ");

            }

            //private readonly BlobContainerClient container;


            public async Task UploadChunkAsync(ReadOnlyMemory<byte> chunk, ChunkHash hash)
            {
                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
                //var bsc = new BlobContainerClient(connectionString, options.Container);
                //await bsc.UploadBlobAsync(hash.Value.ToString(), new BinaryData(chunk));


                var x = new BlobClient(connectionString, options.Container, hash.Value.ToString());
                await x.UploadAsync(new BinaryData(chunk), new BlobUploadOptions { AccessTier = AccessTier.Cool, TransferOptions = new StorageTransferOptions { MaximumConcurrency = 16 } });
                //x.DownloadTo()
            }
            public async Task UploadChunkAsync(Stream s, ManifestHash hash)
            {
                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
                var x = new BlobClient(connectionString, options.Container, hash.Value.ToString());
                await x.UploadAsync(s, new BlobUploadOptions { AccessTier = AccessTier.Cool, TransferOptions = new StorageTransferOptions { MaximumConcurrency = 16 } });

                //var bsc = new BlobContainerClient(connectionString, options.Container);
                //await bsc.UploadBlobAsync(hash.Value.ToString(), s);

            }
        }


    }


    internal class ChunkBlock : BlockingCollectionTaskBlockBase<BinaryFile>
    {
        public ChunkBlock(ILogger<ChunkBlock> logger,
            Func<BlockingCollection<BinaryFile>> sourceFunc,
            int maxDegreeOfParallelism,
            Chunker chunker,
            Action<BinaryFile, IChunkFile[]> chunkedBinary,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.chunker = chunker;
            this.chunkedBinary = chunkedBinary;
        }

        private readonly Chunker chunker;
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
            Func<BlockingCollection<IChunkFile>> sourceFunc,
            Repository repo,
            Action<IChunkFile> chunkToUpload,
            Action<ChunkHash> chunkAlreadyUploaded,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, done: done)
        {
            this.repo = repo;
            this.chunkToUpload = chunkToUpload;
            this.chunkAlreadyUploaded = chunkAlreadyUploaded;
        }

        private readonly Repository repo;
        private readonly Action<IChunkFile> chunkToUpload;
        private readonly Action<ChunkHash> chunkAlreadyUploaded;

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
        private readonly List<ChunkHash> creating = new();

        private async Task<bool> ChunkExists(ChunkHash h)
        {
            return await repo.ChunkExists(h); //TODO: CACHE RESULTS
        }
    }


    internal class EncryptChunkBlock : BlockingCollectionTaskBlockBase<IChunkFile>
    {
        public EncryptChunkBlock(ILogger<EncryptChunkBlock> logger,
            Func<BlockingCollection<IChunkFile>> sourceFunc,
            int maxDegreeOfParallelism,
            TempDirectoryAppSettings tempDirAppSettings,
            IEncrypter encrypter,
            Action<EncryptedChunkFile> chunkEncrypted,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
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

            var targetFile = new FileInfo(Path.Combine(tempDirAppSettings.TempDirectoryFullName, "encryptedchunks", $"{chunkFile.Hash.Value}{EncryptedChunkFile.Extension}"));

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
            Func<BlockingCollection<EncryptedChunkFile>> sourceFunc,
            AzCopyAppSettings azCopyAppSettings,
            Action<EncryptedChunkFile[]> batchForUpload,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, done: done)
        {
            this.azCopyAppSettings = azCopyAppSettings;
            this.batchForUpload = batchForUpload;
        }

        private readonly AzCopyAppSettings azCopyAppSettings;
        private readonly Action<EncryptedChunkFile[]> batchForUpload;

        protected override Task TaskBodyImplAsync(BlockingCollection<EncryptedChunkFile> source)
        {
            var uploadBatch = new List<EncryptedChunkFile>();

            while (!source.IsCompleted) // loop until the source is empty and no more elements will be added
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
                    else if (source.IsCompleted) //this will be the final block
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
            Func<BlockingCollection<EncryptedChunkFile[]>> sourceFunc,
            int maxDegreeOfParallelism,
            Repository repo,
            AccessTier tier,
            Action<ChunkHash[]> chunkUploaded,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.repo = repo;
            this.tier = tier;
            this.chunkUploaded = chunkUploaded;
        }

        private readonly Repository repo;
        private readonly AccessTier tier;
        private readonly Action<ChunkHash[]> chunkUploaded;

        protected override Task ForEachBodyImplAsync(EncryptedChunkFile[] ecfs)
        {
            logger.LogInformation($"Uploading batch..."); // Remaining Batches queue depth: {_block!.Value.InputCount}");

            //Upload the chunks
            repo.Upload(ecfs, tier);

            //Delete the (temporary) encrypted chunk files
            foreach (var ecf in ecfs)
                ecf.Delete();

            logger.LogInformation($"Uploading batch... done");

            //foreach (var chunk in ecfs)
                chunkUploaded(ecfs.Select(c => c.Hash).ToArray());

            return Task.CompletedTask;
        }
    }


    internal class CreateManifestBlock : BlockingCollectionTaskBlockBase<(ManifestHash ManifestHash, ChunkHash[] ChunkHashes)>
    {
        public CreateManifestBlock(ILogger<CreateManifestBlock> logger,
            Func<BlockingCollection<(ManifestHash ManifestHash, ChunkHash[] ChunkHashes)>> sourceFunc,
            int maxDegreeOfParallelism,
            Repository repo,
            Action<ManifestHash> manifestCreated,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.repo = repo;
            this.manifestCreated = manifestCreated;
        }

        private readonly Repository repo;
        private readonly Action<ManifestHash> manifestCreated;

        protected override async Task ForEachBodyImplAsync((ManifestHash ManifestHash, ChunkHash[] ChunkHashes) item)
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
            Func<BlockingCollection<BinaryFile>> sourceFunc,
            int maxDegreeOfParallelism,
            PointerService pointerService,
            Action<BinaryFile> succesfullyBackedUp,
            Action<PointerFile> pointerFileCreated,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
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
            Func<BlockingCollection<PointerFile>> sourceFunc,
            int maxDegreeOfParallelism,
            Repository repo,
            DateTime versionUtc,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.repo = repo;
            this.versionUtc = versionUtc;
        }

        private readonly Repository repo;
        private readonly DateTime versionUtc;

        protected override async Task ForEachBodyImplAsync(PointerFile pointerFile)
        {
            logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'...");

            var r = await repo.CreatePointerFileEntryIfNotExistsAsync(pointerFile, versionUtc);

            switch (r)
            {
                case Repository.CreatePointerFileEntryResult.Upserted:
                    logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Upserted entry.");
                    break;
                case Repository.CreatePointerFileEntryResult.InsertedDeleted:
                    logger.LogInformation($"Upserting PointerFile entry for '{pointerFile.RelativeName}'... done. Inserted 'deleted' entry.");
                    break;
                case Repository.CreatePointerFileEntryResult.NoChange:
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
            Func<BlockingCollection<BinaryFile>> sourceFunc,
            int maxDegreeOfParallelism,
            bool removeLocal,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
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


    internal class CreateDeletedPointerFileEntryForDeletedPointerFilesBlock : BlockingCollectionTaskBlockBase<PointerFileEntry>
    {
        public CreateDeletedPointerFileEntryForDeletedPointerFilesBlock(ILogger<CreateDeletedPointerFileEntryForDeletedPointerFilesBlock> logger,
            Func<Task<BlockingCollection<PointerFileEntry>>> sourceFunc,
            int maxDegreeOfParallelism,
            Repository repo,
            DirectoryInfo root,
            PointerService pointerService,
            DateTime versionUtc,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, maxDegreeOfParallelism: maxDegreeOfParallelism, done: done)
        {
            this.repo = repo;
            this.root = root;
            this.pointerService = pointerService;
            this.versionUtc = versionUtc;
        }

        private readonly Repository repo;
        private readonly DirectoryInfo root;
        private readonly PointerService pointerService;
        private readonly DateTime versionUtc;

        protected override async Task ForEachBodyImplAsync(PointerFileEntry pfe)
        {
            if (!pfe.IsDeleted &&
                pointerService.GetPointerFile(root, pfe) is null && 
                pointerService.GetBinaryFile(root, pfe, ensureCorrectHash: false) is null) //PointerFileEntry is marked as exists and there is no PointerFile and there is no BinaryFile (only on PointerFile may not work since it may still be in the pipeline to be created)
            {
                logger.LogInformation($"The pointer or binary for '{pfe.RelativeName}' no longer exists locally, marking entry as deleted");
                await repo.CreateDeletedPointerFileEntryAsync(pfe, versionUtc);
            }
        }
    }


    internal class ExportToJsonBlock : TaskBlockBase<BlockingCollection<PointerFileEntry>> //! must be single threaded hence TaskBlockBase
    {
        public ExportToJsonBlock(ILogger<ExportToJsonBlock> logger,
            Func<Task<BlockingCollection<PointerFileEntry>>> sourceFunc,
            Repository repo,
            DateTime versionUtc,
            Action done) : base(logger: logger, sourceFunc: sourceFunc, done: done) 
        {
            this.repo = repo;
            this.versionUtc = versionUtc;
        }

        private readonly Repository repo;
        private readonly DateTime versionUtc;


        protected override async Task TaskBodyImplAsync(BlockingCollection<PointerFileEntry> source)
        {
            logger.LogInformation($"Writing state to JSON...");

            using Stream file = File.Create($"arius-state-{versionUtc.ToLocalTime():yyyyMMdd-HHmmss}.json");
            var writer = new Utf8JsonWriter(file, new JsonWriterOptions() { Indented = true });
            writer.WriteStartArray();

            // See https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-converters-how-to

            foreach (var pfe in source
                    //.AsParallel().WithDegreeOfParallelism(8)
                    //.AsEnumerable()) 
                    .GetConsumingEnumerable())
            {
                var chs = await repo.GetChunkHashesForManifestAsync(pfe.ManifestHash);
                var entry = new PointerFileEntryWithChunkHashes(pfe, chs);

                //lock (writer)
                //{ 
                JsonSerializer.Serialize(writer, entry /*entry*/, new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default });
                //}
            }

            writer.WriteEndArray();
            await writer.FlushAsync();

            logger.LogInformation($"Writing state to JSON... done");
        }

        private struct PointerFileEntryWithChunkHashes
        {
            public PointerFileEntryWithChunkHashes(PointerFileEntry pfe, ChunkHash[] chs)
            {
                this.pfe = pfe;
                this.chs = chs;
            }

            private readonly PointerFileEntry pfe;
            private readonly ChunkHash[] chs;

            public string ManifestHash => pfe.ManifestHash.Value;
            public IEnumerable<string> ChunkHashes => chs.Select(h => h.Value);
            public string RelativeName => pfe.RelativeName;
            public DateTime VersionUtc => pfe.VersionUtc;
            public bool IsDeleted => pfe.IsDeleted;
            public DateTime? CreationTimeUtc => pfe.CreationTimeUtc;
            public DateTime? LastWriteTimeUtc => pfe.LastWriteTimeUtc;
        }
    }


    internal class ValidateBlock
    {
        public ValidateBlock(ILogger<ExportToJsonBlock> logger,
            Func<BlockingCollection<PointerFileEntry>> sourceFunc,
            Repository repo,
            DateTime versionUtc,
            Action start,
            Action done)
        {
            //logger.LogInformation($"Validating {pointerFile.FullName}...");

            //logger.LogWarning($"Validating {pointerFile.FullName}... - Not yet implemented");

            ////    // Validate the manifest
            ////    var chunkHashes = await repo.GetChunkHashesAsync(pointerFile.Hash);

            ////    if (!chunkHashes.Any())
            ////        throw new InvalidOperationException($"Manifest {pointerFile.Hash} (of PointerFile {pointerFile.FullName}) contains no chunks");

            ////    double length = 0;
            ////    foreach (var chunkHash in chunkHashes)
            ////    {
            ////        var cb = repo.GetChunkBlobByHash(chunkHash, false);
            ////        length += cb.Length;
            ////    }

            ////    var bfi = pointerFile.BinaryFileInfo;
            ////    if (bfi.Exists)
            ////    {
            ////        //TODO if we would know the EXACT/uncompressed size from the PointerFileEntry - use that
            ////        if (bfi.Length / length < 0.9)
            ////            throw new InvalidOperationException("something is wrong");
            ////    }
            ////    else
            ////    {
            ////        //TODO if we would know the expected size from the PointerFileEntry - use that
            ////        if (length == 0)
            ////            throw new InvalidOperationException("something is wrong");
            ////    }

            //logger.LogInformation($"Validating {pointerFile.FullName}... OK!");
        }

    }
}
