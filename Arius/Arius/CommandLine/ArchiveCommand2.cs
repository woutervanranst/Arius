using Arius.Services;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Models;
using Azure.Storage.Blobs;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HashValue = Arius.Services.HashValue;

namespace Arius
{
    internal class ArchiveCommandExecutor2 : ICommandExecutor
    {
        public ArchiveCommandExecutor2(ICommandExecutorOptions options,
            ILogger<ArchiveCommandExecutor> logger,
            LocalRootRepository localRoot,
            AriusRepository2 ariusRepository,

            IHashValueProvider h,
            IChunker c,
            IEncrypter e)
        {
            _options = (ArchiveOptions)options;
            _logger = logger;
            //_localRoot = localRoot;
            _root = new DirectoryInfo(localRoot.FullName);
            _ariusRepository = ariusRepository;

            _hvp = h;
            _chunker = c;
            _encrypter = e;
        }

        private readonly ArchiveOptions _options;
        private readonly ILogger<ArchiveCommandExecutor> _logger;

        private readonly DirectoryInfo _root;
        private readonly IHashValueProvider _hvp;
        private readonly IChunker _chunker;
        private readonly IEncrypter _encrypter;
        private readonly AriusRepository2 _ariusRepository;


        public int Execute()
        {
            var version = DateTime.Now;

            var fastHash = true;

            //Dowload db etc
            using (var db = new Context())
            {
                db.Database.EnsureCreated();
            }

            var indexDirectoryBlock = new TransformManyBlock<DirectoryInfo, AriusArchiveItem>(
                di => IndexDirectory(di),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 }
                );

            var addHashBlock = new TransformBlock<AriusArchiveItem, AriusArchiveItem>(
                item => (AriusArchiveItem)AddHash((dynamic) item, fastHash),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded }
            );

            var castToBinaryBlock = new TransformBlock<AriusArchiveItem, BinaryFile>(item => (BinaryFile)item);
            var castToPointerBlock = new TransformBlock<AriusArchiveItem, PointerFile>(item => (PointerFile)item);

            //var processedOrProcessingBinaries = new List<HashValue>(); 
            //var allBinaryFiles = new ConcurrentDictionary<HashValue, BinaryFile>();

            var uploadedManifestHashes = new List<HashValue>();
            var uploadingManifestHashes = new List<HashValue>();

            using (var db = new Context())
            {
                uploadingManifestHashes.AddRange(db.Manifests.Select(m => new HashValue() {Value = m.HashValue}));
            }


            var manifestBeforePointers = new ConcurrentDictionary<HashValue, ConcurrentBag<BinaryFile>>(); //Key = HashValue van de Manifest
            var chunksThatNeedToBeUploadedBeforeManifestCanBeCreated = new ConcurrentDictionary<HashValue, ConcurrentDictionary<HashValue, bool>>(); //Key = HashValue van de Manifest, ValueDict = HashValue van de Chunks
            

            var getChunksBlock = new TransformManyBlock<BinaryFile, ChunkFile2>(binaryFile =>
                {
                    Console.WriteLine("Chunking BinaryFile " + binaryFile.Name);

                    var addChunks = false;

                    lock (uploadedManifestHashes)
                    {
                        lock (uploadingManifestHashes)
                        {
                            var h = binaryFile.Hash!.Value;
                            if (!uploadedManifestHashes.Union(uploadingManifestHashes).Contains(h))
                            {
                                uploadingManifestHashes.Add(h);
                                addChunks = true;
                            }
                        }
                    }

                    // Add this binaryFile to the list of pointers to be created, once this manifest is created
                    var bag = manifestBeforePointers.GetOrAdd(binaryFile.Hash.Value, new ConcurrentBag<BinaryFile>());
                    bag.Add(binaryFile);

                    if (addChunks)
                    {
                        // Process the chunks
                        var chunks = AddChunks(binaryFile);

                        chunksThatNeedToBeUploadedBeforeManifestCanBeCreated.TryAdd(
                            binaryFile.Hash!.Value,
                            new ConcurrentDictionary<HashValue, bool>(
                                chunks.Select(a => 
                                    new KeyValuePair<HashValue, bool>(a.Hash!.Value, false))));

                        return chunks;
                    }
                    else
                        return Enumerable.Empty<ChunkFile2>();
                },
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded}
            );

            var encryptChunksBlock = new TransformBlock<ChunkFile2, EncryptedChunkFile2>(
                chunkFile => Encrypt(chunkFile),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded });


            
            var uploadedOrUploadingChunks = _ariusRepository.GetAllChunkBlobItems().ToDictionary(a => a.Hash, a => a);


            var uploadEncryptedChunksBlock = new TransformBlock<EncryptedChunkFile2, RemoteEncryptedChunkBlobItem2>(
                encryptedChunkFile =>
                {
                    bool upload = false;
                    var h = encryptedChunkFile.Hash!.Value;

                    lock (uploadedOrUploadingChunks)
                    {
                        if (!uploadedOrUploadingChunks.ContainsKey(h))
                        {
                            uploadedOrUploadingChunks.Add(h, null);
                            upload = true;
                        }
                    }

                    if (upload)
                    {
                        //Upload the file
                        var x = _ariusRepository.Upload(encryptedChunkFile, _options.Tier);

                        //Delete the file
                        encryptedChunkFile.Delete();

                        uploadedOrUploadingChunks[h] = x;
                    }

                    return uploadedOrUploadingChunks[h];
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
                var chunks = binaryFiles.Single(bf => bf.Chunks != null && bf.Chunks.Length > 0)
                    .Chunks.Select(c => c.Hash!.Value).ToArray();

                // Create the manifest
                using (var db = new Context())
                {
                    db.Manifests.Add(new Manifest2
                    {
                        HashValue = manifestHash.Value,
                        Chunks = chunks.Select((hv, i) => new OrderedChunk
                        {
                            ManifestHashValue = manifestHash.Value,
                            ChunkHashValue = hv.Value, 
                            Order = i
                        }).ToList()
                    });
                    db.SaveChanges();
                }

                // Return the items that need to be added to the manifest
                return binaryFiles;
            });

            var createPointersBlock = new TransformBlock<BinaryFile, PointerFile>(binaryFile =>
            {
                var p = binaryFile.CreatePointerFile();

                return p;
            });

            var updateManifestBlock = new ActionBlock<PointerFile>(pointerFile =>
            {
                // Update the manifest
                using (var db = new Context())
                {
                    var me = db.Manifests.Single(m => m.HashValue == pointerFile.Hash!.Value.Value);

                    var e = new PointerFileEntry
                    {
                        RelativeName = Path.GetRelativePath(_root.FullName, pointerFile.FileFullName),
                        Version = version,
                        CreationTimeUtc = File.GetCreationTimeUtc(pointerFile.FileFullName), //TODO
                        LastWriteTimeUtc = File.GetLastWriteTimeUtc(pointerFile.FileFullName),
                        IsDeleted = false
                    };
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
                castToBinaryBlock,
                new DataflowLinkOptions { PropagateCompletion = true },
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


            castToBinaryBlock.LinkTo(
                getChunksBlock,
                new DataflowLinkOptions { PropagateCompletion = true });

            getChunksBlock.LinkTo(
                encryptChunksBlock,
                new DataflowLinkOptions { PropagateCompletion = true });


            encryptChunksBlock.LinkTo(
                uploadEncryptedChunksBlock,
                new DataflowLinkOptions() {PropagateCompletion = true});


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



            //updateManifestBlock.LinkTo(
            //    endBlock,
            //    new DataflowLinkOptions() {PropagateCompletion = true});


            indexDirectoryBlock.Post(_root);
            indexDirectoryBlock.Complete();

            Task.WhenAll(createManifestBlock.Completion, castToPointerBlock.Completion)
                .ContinueWith(_ => updateManifestBlock.Complete());

            updateManifestBlock.Completion.Wait();

            using (var db = new Context())
            {
                //Not parallel foreach since DbContext is not thread safe

                foreach (var m in db.Manifests.Include(m => m.Entries))
                {
                    foreach (var e in m.GetLastEntries(false).Where(e => e.Version != version))
                    {
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

            using (var db = new Context())
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

        private AriusArchiveItem AddHash(PointerFile f, bool _)
        {
            Console.WriteLine("Hashing PointerFile " + f.Name);

            f.Hash = ReadHashFromPointerFile(f.FileFullName);

            Console.WriteLine("Hashing PointerFile " + f.Name + " done");

            return f;
        }

       

        private AriusArchiveItem AddHash(BinaryFile f, bool fastHash)
        {
            Console.WriteLine("Hashing BinaryFile " + f.Name);

            var h = default(HashValue?);

            if (fastHash)
            {
                var pointerFileFullName = f.GetPointerFileFullName();
                if (File.Exists(pointerFileFullName))
                    h = ReadHashFromPointerFile(pointerFileFullName);
            }

            if (!h.HasValue)
                h = ((SHA256Hasher) _hvp).GetHashValue(f); //TODO remove cast)

            f.Hash = h;

            Console.WriteLine("Hashing BinaryFile " + f.Name + " done");

            return f;
        }

        private static HashValue ReadHashFromPointerFile(string fileName)
        {
            return new() { Value = File.ReadAllText(fileName) };
        }

        public ChunkFile2[] AddChunks(BinaryFile f)
        {
            Console.WriteLine("Chunking BinaryFile " + f.Name);

            var cs = ((Chunker)_chunker).Chunk(f);
            f.Chunks = cs;

            Console.WriteLine("Chunking BinaryFile " + f.Name + " done");

            return cs;
        }

        private EncryptedChunkFile2 Encrypt(ChunkFile2 f)
        {
            Console.WriteLine("Encrypting ChunkFile2 " + f.Name);

            var ecf = ((SevenZipCommandlineEncrypter)_encrypter).Encrypt(f);
            ecf.Hash = f.Hash;

            Console.WriteLine("Encrypting ChunkFile2 " + f.Name + " done");

            return ecf;
        }
    }

    internal static class ManifestService2
    {
        /// <summary>
        /// Get the last entries per RelativeName
        /// </summary>
        /// <param name="includeLastDeleted">include deleted items</param>
        /// <returns></returns>
        public static IEnumerable<PointerFileEntry> GetLastEntries(this Manifest2 m, bool includeLastDeleted = false)
        {
            var r = m.Entries
                .GroupBy(e => e.RelativeName)
                .Select(g => g.OrderBy(e => e.Version).Last());

            if (includeLastDeleted)
                return r;
            else
                return r.Where(e => !e.IsDeleted);
        }
    }

    internal abstract class AriusArchiveItem
    {
        private readonly FileInfo _fi;

        protected AriusArchiveItem(FileInfo fi)
        {
            _fi = fi;
        }

        public string FileFullName => _fi.FullName;
        public string Name => _fi.Name;
        public DirectoryInfo Directory => _fi.Directory;

        public HashValue? Hash
        {
            get => _hashValue;
            set
            {
                if (_hashValue.HasValue)
                    throw new InvalidOperationException("CAN ONLY BE SET ONCE");
                _hashValue = value;
            }
        }
        private HashValue? _hashValue;

        public void Delete()
        {
            _fi.Delete();
        }
    }

    internal class PointerFile : AriusArchiveItem
    {
        public const string Extension = ".pointer.arius";

        public PointerFile(FileInfo fi) : base(fi) { }

        public PointerFile(FileInfo fi, HashValue manifestHash) : base(fi)
        {
            this.Hash = manifestHash;
        }
    }

    internal class BinaryFile : AriusArchiveItem
    {
        public BinaryFile(FileInfo fi) : base(fi) { }

        public ChunkFile2[] Chunks { get; set; }
    }

    internal class ChunkFile2 : AriusArchiveItem
    {
        public ChunkFile2(FileInfo fi) : base(fi) { }

        public EncryptedChunkFile2 EncryptedChunkFile { get; set; }
    }

    internal class EncryptedChunkFile2 : AriusArchiveItem
    {
        public const string Extension = ".7z.arius";

        public EncryptedChunkFile2(FileInfo fi) : base(fi) { }
    }


    internal class AriusRepository2
    {
        private readonly IBlobCopier _blobCopier;

        public AriusRepository2(ICommandExecutorOptions options, IBlobCopier b)
        {
            _blobCopier = b;

            var o = (IRemoteChunkRepositoryOptions)options;

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";
            var bsc = new BlobServiceClient(connectionString);
            _bcc = bsc.GetBlobContainerClient(o.Container);
        }

        private readonly BlobContainerClient _bcc;

        private const string EncryptedChunkDirectoryName = "chunks";


        public IEnumerable<RemoteEncryptedChunkBlobItem2> GetAllChunkBlobItems()
        {
            //var k = _bcc.GetBlobs(prefix: EncryptedChunkDirectoryName + "/").ToList();

            return _bcc.GetBlobs(prefix: EncryptedChunkDirectoryName + "/")
                .Select(bi => new RemoteEncryptedChunkBlobItem2(bi));
        }

        public RemoteEncryptedChunkBlobItem2 GetByName(string name, string folder = EncryptedChunkDirectoryName)
        {
            var bi = _bcc
                .GetBlobs(prefix: $"{folder}/{name}", traits: BlobTraits.Metadata & BlobTraits.CopyStatus)
                .Single();

            return new RemoteEncryptedChunkBlobItem2(bi);
        }

        public RemoteEncryptedChunkBlobItem2 Upload(EncryptedChunkFile2 ecf, AccessTier tier)
        {
            ((AzCopier)_blobCopier).Upload(ecf, tier,EncryptedChunkDirectoryName, false);

            return GetByName(ecf.Name);
        }
    }

    internal abstract class Blob2
    {
        protected Blob2(
            //IRepository root, 
            BlobItem blobItem //, 
            //Func<IBlob, HashValue> hashValueProvider
            )
        {
            //_root = root;
            _bi = blobItem;

            //_hash = new Lazy<HashValue>(() => hashValueProvider(this)); //NO method groep > moet lazily evaluated zijn
        }

        //protected readonly IRepository _root;
        protected readonly BlobItem _bi;
        //private readonly Lazy<HashValue> _hash;


        public string FullName => _bi.Name;
        public string Name => _bi.Name.Split('/').Last(); //TODO werkt titi met alle soorten repos?
        public string Folder => _bi.Name.Split('/').First();
        public string NameWithoutExtension => Name.TrimEnd(Extension);
        public abstract HashValue Hash { get; }
        protected abstract string Extension { get; }
    }

    class RemoteEncryptedChunkBlobItem2 : Blob2
    {
        public RemoteEncryptedChunkBlobItem2(BlobItem bi) : base(bi)
        {
        }

        public override HashValue Hash => new HashValue { Value = NameWithoutExtension };
        protected override string Extension => ".7z.arius";
    }


    internal class Context : DbContext
    {
        public DbSet<Manifest2> Manifests { get; set; }
        //public DbSet<OrderedChunk> Chunks { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(@"Data Source=c:\arius.db");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var me = modelBuilder.Entity<Manifest2>();
            me.HasKey(m => m.HashValue);
            me.HasMany(m => m.Entries);
            me.HasMany(m => m.Chunks);

            var pfee = modelBuilder.Entity<PointerFileEntry>();
            pfee.HasKey(pfe => new { pfe.RelativeName, pfe.Version});

            var oce = modelBuilder.Entity<OrderedChunk>();
            oce.HasKey(oc => new {oc.ManifestHashValue, oc.ChunkHashValue});

        }
    }


    internal class Manifest2
    {
        public string HashValue { get; set; }

        public List<PointerFileEntry> Entries { get; init; } = new();
        public List<OrderedChunk> Chunks { get; init; } = new();
    }

    internal class PointerFileEntry
    {
        public string RelativeName { get; set; }
        public DateTime Version { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? CreationTimeUtc { get; set; }
        public DateTime? LastWriteTimeUtc { get; set; }
    }

    //internal class Chunk
    //{
    //    public string HashValue { get; set; }
    //}

    internal class OrderedChunk
    {
        public string ManifestHashValue { get; set; }
        public string ChunkHashValue { get; set; }
        public int Order { get; set; }
    }
}
