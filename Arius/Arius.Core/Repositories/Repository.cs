using Arius.Core.Extensions;
using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PostSharp.Constraints;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository : IDisposable
{
    private readonly ILogger<Repository>    logger;
    private readonly IAriusDbContextFactory dbContextFactory;
    private readonly BlobContainerClient    container;

    [ComponentInternal("Arius.Cli.Tests")] // added only for Moq
    public Repository()
    {
    }

    [ComponentInternal(typeof(RepositoryBuilder))]
    public Repository(ILogger<Repository> logger, IRepositoryOptions options, IAriusDbContextFactory dbContextFactory, BlobContainerClient container)
    {
        this.logger           = logger;
        this.dbContextFactory = dbContextFactory;
        this.container        = container;
        this.Options          = options;
    }

    public IRepositoryOptions Options { get; }

    // --------- STATES ---------

    internal const string StateDbsFolderName = "states";

    private AriusDbContext GetAriusDbContext() => dbContextFactory.GetContext(); // note for testing internal - perhaps use the IAriusDbContextFactory directly?

    public async Task SaveStateToRepository(DateTime versionUtc)
    {
        await dbContextFactory.SaveAsync(versionUtc);
    }

    // --------- BINARY PROPERTIES ---------

    internal async Task<BinaryProperties> CreateBinaryPropertiesAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount)
    {
        var bp = new BinaryProperties()
        {
            Hash              = bf.BinaryHash,
            OriginalLength    = bf.Length,
            ArchivedLength    = archivedLength,
            IncrementalLength = incrementalLength,
            ChunkCount        = chunkCount
        };

        await using var db = GetAriusDbContext();
        await db.BinaryProperties.AddAsync(bp);
        await db.SaveChangesAsync();

        return bp;
    }

    public async Task<BinaryProperties> GetBinaryPropertiesAsync(BinaryHash bh)
    {
        try
        {
            await using var db = GetAriusDbContext();
            return db.BinaryProperties.Single(bp => bp.Hash == bh);
        }
        catch (InvalidOperationException e) when (e.Message == "Sequence contains no elements")
        {
            throw new InvalidOperationException($"Could not find BinaryProperties for '{bh}'", e);
        }
    }

    // --------- BINARIES ---------

    /// <summary>
    /// Get the count of (distinct) BinaryHashes
    /// </summary>
    /// <returns></returns>
    public async Task<int> CountBinariesAsync()
    {
        await using var db = GetAriusDbContext();
        return await db.BinaryProperties.CountAsync();
        //return await db.PointerFileEntries
        //    .Select(pfe => pfe.BinaryHash)
        //    .Distinct()
        //    .CountAsync();
    }

    public async Task<bool> BinaryExistsAsync(BinaryHash bh)
    {
        await using var db = GetAriusDbContext();
        return await db.BinaryProperties.AnyAsync(bp => bp.Hash == bh);
    }

    public async Task<long> TotalBinaryIncrementalLengthAsync()
    {
        await using var db = GetAriusDbContext();
        return await db.BinaryProperties.SumAsync(bp => bp.IncrementalLength);
    }

    // --------- CHUNKLISTS ---------

    private const string ChunkListsFolderName = "chunklists";

    private const string JsonGzipContentType = "application/json+gzip";

    internal string GetChunkListBlobName(BinaryHash bh) => $"{ChunkListsFolderName}/{bh.Value}";

    internal async Task CreateChunkListAsync(BinaryHash bh, ChunkHash[] chunkHashes)
    {
        /* When writing to blob
         * Logging
         * Check if exists
         * Check tag
         * error handling around write / delete on fail
         * log
         */

        logger.LogDebug($"Creating ChunkList for '{bh.ToShortString()}'...");

        if (chunkHashes.Length == 1)
            return; //do not create a ChunkList for only one ChunkHash

        var bbc = container.GetBlockBlobClient(GetChunkListBlobName(bh));

        RestartUpload:

        try
        {
            using (var ts = await bbc.OpenWriteAsync(overwrite: true, options: ThrowOnExistOptions)) //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
            {
                using var gzs = new GZipStream(ts, CompressionLevel.Optimal);
                await JsonSerializer.SerializeAsync(gzs, chunkHashes.Select(cf => cf.Value));
            }

            await bbc.SetAccessTierAsync(AccessTier.Cool);
            await bbc.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = JsonGzipContentType });

            logger.LogInformation($"Creating ChunkList for '{bh.ToShortString()}'... done with {chunkHashes.Length} chunks");
        }
        catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict)
        {
            // The blob already exists
            try
            {
                var p = (await bbc.GetPropertiesAsync()).Value;
                if (p.ContentType != JsonGzipContentType || p.ContentLength == 0)
                {
                    logger.LogWarning($"Corrupt ChunkList for {bh}. Deleting and uploading again");
                    await bbc.DeleteAsync();

                    goto RestartUpload;
                }
                else
                {
                    // gracful handling if the chunklist already exists
                    //throw new InvalidOperationException($"ChunkList for '{bh.ToShortString()}' already exists");
                    logger.LogWarning($"A valid ChunkList for '{bh}' already existed, perhaps in a previous/crashed run?");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Exception while reading properties of chunklist {bh}");
                throw;
            }
        }
        catch (Exception e)
        {
            var e2 = new InvalidOperationException($"Error when creating ChunkList {bh.ToShortString()}. Deleting...", e);
            logger.LogError(e2);
            await bbc.DeleteAsync();
            logger.LogDebug("Succesfully deleted");

            throw e2;
        }
    }

    internal async Task<ChunkHash[]> GetChunkListAsync(BinaryHash bh)
    {
        logger.LogDebug($"Getting ChunkList for '{bh.ToShortString()}'...");

        if ((await GetBinaryPropertiesAsync(bh)).ChunkCount == 1)
            return ((ChunkHash)bh).AsArray();

        var chs = default(ChunkHash[]);

        try
        {
            var bbc = container.GetBlockBlobClient(GetChunkListBlobName(bh));

            if ((await bbc.GetPropertiesAsync()).Value.ContentType != JsonGzipContentType)
                throw new InvalidOperationException($"ChunkList '{bh}' does not have the '{JsonGzipContentType}' ContentType and is potentially corrupt");

            using (var ss = await bbc.OpenReadAsync())
            {
                using var gzs = new GZipStream(ss, CompressionMode.Decompress);
                chs = (await JsonSerializer.DeserializeAsync<IEnumerable<string>>(gzs))
                    !.Select(chv => new ChunkHash(chv))
                    .ToArray();

                logger.LogInformation($"Getting chunks for binary '{bh.ToShortString()}'... found {chs.Length} chunk(s)");

                return chs;
            }
        }
        catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
        {
            throw new InvalidOperationException($"ChunkList for '{bh.ToShortString()}' does not exist");
        }
    }

    // --------- CHUNKS ---------

    internal const string ChunkFolderName           = "chunks";
    internal const string RehydratedChunkFolderName = "chunks-rehydrated";

    public IAsyncEnumerable<ChunkBlobBase> GetAllChunkBlobs()
    {
        return container.GetBlobsAsync(prefix: $"{ChunkFolderName}/")
            .Select(bi => ChunkBlobBase.GetChunkBlob(container, bi));
    }

    /// <summary>
    /// Get the RemoteEncryptedChunkBlobItem - either from permanent cold storage or from temporary rehydration storage
    /// If the chunk does not exist, throws an InvalidOperationException
    /// If requireHydrated is true and the chunk does not exist in cold storage, returns null
    /// </summary>
    public ChunkBlobBase GetChunkBlobByHash(ChunkHash chunkHash, bool requireHydrated)
    {
        var blobName = GetChunkBlobName(ChunkFolderName, chunkHash);
        var cb1      = GetChunkBlobByName(blobName);

        if (cb1 is null)
            throw new InvalidOperationException($"Could not find Chunk {chunkHash.Value}");

        // if we don't need a hydrated chunk, return this one
        if (!requireHydrated)
            return cb1;

        // if we require a hydrated chunk, and this one is hydrated, return this one
        if (requireHydrated && cb1.Downloadable)
            return cb1;

        blobName = GetChunkBlobName(RehydratedChunkFolderName, chunkHash);
        var cb2 = GetChunkBlobByName(blobName);

        if (cb2 is null || !cb2.Downloadable)
        {
            // no hydrated chunk exists
            logger.LogDebug($"No hydrated chunk found for {chunkHash}");
            return null;
        }
        else
            return cb2;
    }

    private string GetChunkBlobName(string folder, ChunkHash chunkHash) => GetChunkBlobFullName(folder, chunkHash.Value);
    private string GetChunkBlobFullName(string folder, string name)     => $"{folder}/{name}";

    /// <summary>
    /// Get a ChunkBlobBase in the given folder with the given name.
    /// Return null if it doesn't exist.
    /// </summary>
    /// <param name="folder"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    internal ChunkBlobBase GetChunkBlobByName(string folder, string name) => GetChunkBlobByName(GetChunkBlobFullName(folder, name));

    internal ChunkBlobBase GetChunkBlobByName(BlobItem bi) => GetChunkBlobByName(bi.Name);

    /// <summary>
    /// Get a ChunkBlobBase by FullName.
    /// Return null if it doesn't exist.
    /// </summary>
    /// <returns></returns>
    internal ChunkBlobBase GetChunkBlobByName(string blobName)
    {
        try
        {
            var bc = container.GetBlobClient(blobName);
            var cb = ChunkBlobBase.GetChunkBlob(bc);
            return cb;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public async Task<bool> ChunkExistsAsync(ChunkHash chunkHash)
    {
        return await container.GetBlobClient(GetChunkBlobName(ChunkFolderName, chunkHash)).ExistsAsync();
    }

    public async Task HydrateChunkAsync(ChunkBlobBase blobToHydrate)
    {
        logger.LogDebug($"Checking hydration for chunk {blobToHydrate.ChunkHash.ToShortString()}");

        if (blobToHydrate.AccessTier == AccessTier.Hot ||
            blobToHydrate.AccessTier == AccessTier.Cool)
            throw new InvalidOperationException($"Calling Hydrate on a blob that is already hydrated ({blobToHydrate.Name})");

        var hydratedItem = container.GetBlobClient($"{RehydratedChunkFolderName}/{blobToHydrate.Name}");

        if (!await hydratedItem.ExistsAsync())
        {
            //Start hydration
            await hydratedItem.StartCopyFromUriAsync(
                blobToHydrate.Uri,
                new BlobCopyFromUriOptions { AccessTier = AccessTier.Cool, RehydratePriority = RehydratePriority.Standard });

            logger.LogInformation($"Hydration started for '{blobToHydrate.ChunkHash.ToShortString()}'");
        }
        else
        {
            // Get hydration status
            // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

            var status = (await hydratedItem.GetPropertiesAsync()).Value.ArchiveStatus;
            if (status == "rehydrate-pending-to-cool" || status == "rehydrate-pending-to-hot")
                logger.LogInformation($"Hydration pending for '{blobToHydrate.ChunkHash.ToShortString()}'");
            else if (status == null)
                logger.LogInformation($"Hydration done for '{blobToHydrate.ChunkHash.ToShortString()}'");
            else
                throw new ArgumentException($"BlobClient returned an unknown ArchiveStatus {status}");
        }
    }

    public async Task DeleteHydrateFolderAsync()
    {
        logger.LogInformation("Deleting temporary hydration folder");

        await foreach (var bi in container.GetBlobsAsync(prefix: RehydratedChunkFolderName))
        {
            var bc = container.GetBlobClient(bi.Name);
            await bc.DeleteAsync();
        }
    }

    /// <summary>
    /// Upload a (plaintext) chunk to the repository after compressing and encrypting it
    /// </summary>
    /// <returns>Returns the length of the uploaded stream.</returns>
    internal async Task<long> UploadChunkAsync(IChunk chunk, AccessTier tier)
    {
        logger.LogDebug($"Uploading Chunk '{chunk.ChunkHash.ToShortString()}'...");

        var bbc = container.GetBlockBlobClient(GetChunkBlobName(ChunkFolderName, chunk.ChunkHash));

        RestartUpload:

        try
        {
            // v12 with blockBlob.Upload: https://blog.matrixpost.net/accessing-azure-storage-account-blobs-from-c-applications/

            long length;
            await using (var ts = await bbc.OpenWriteAsync(overwrite: true, options: ThrowOnExistOptions)) //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
            {
                await using var ss = await chunk.OpenReadAsync();
                await CryptoService.CompressAndEncryptAsync(ss, ts, Options.Passphrase);
                length = ts.Position;
            }

            await bbc.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = CryptoService.ContentType }); //NOTE put this before SetAccessTier -- once Archived no more operations can happen on the blob

            // Set access tier per policy
            await bbc.SetAccessTierAsync(ChunkBlobBase.GetPolicyAccessTier(tier, length)); //TODO Unit test this: smaller blocks are not put into archive tier

            logger.LogInformation($"Uploading Chunk '{chunk.ChunkHash.ToShortString()}'... done");

            return length;
        }
        catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict /*409*/) //icw ThrowOnExistOptions. In case of hot/cool, throws a 409+BlobAlreadyExists. In case of archive, throws a 409+BlobArchived
        {
            // The blob already exists
            try
            {
                var p = (await bbc.GetPropertiesAsync()).Value;
                if (p.ContentType != CryptoService.ContentType || p.ContentLength == 0)
                {
                    logger.LogWarning($"Corrupt chunk {chunk.ChunkHash}. Deleting and uploading again");
                    await bbc.DeleteAsync();

                    goto RestartUpload;
                }
                else
                {
                    // graceful handling if the chunk is already uploaded but it does not yet exist in the database
                    //throw new InvalidOperationException($"Chunk {chunk.Hash} with length {p.ContentLength} and contenttype {p.ContentType} already exists, but somehow we are uploading this again."); //this would be a multithreading issue
                    logger.LogWarning($"A valid Chunk '{chunk.ChunkHash}' already existsted, perhaps in a previous/crashed run?");

                    return p.ContentLength;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Exception while reading properties of chunk {chunk.ChunkHash}");
                throw;
            }
        }
        catch (Exception e)
        {
            var e2 = new InvalidOperationException($"Error while uploading chunk {chunk.ChunkHash}. Deleting...", e);
            logger.LogError(e2); //TODO test this in a unit test
            await bbc.DeleteAsync();
            logger.LogDebug("Error while uploading chunk. Deleting potentially corrupt chunk... Success.");

            throw e2;
        }
    }


    // --------- BLA ---------

    public async IAsyncEnumerable<(PointerFileEntry PointerFileEntry, BinaryProperties BinaryProperties)> GetPointerFileEntriesWithBinaryPropertiesAsync(string relativeNamePrefix)
    {
        throw new NotImplementedException();

        // TODO: use db.PointerFileEntries.Include(e => e.BinaryProperties)
        // EF Core Migrations

        await using var db = GetAriusDbContext();

        var r = db.PointerFileEntries.Where(pfe => pfe.RelativeName.StartsWith(relativeNamePrefix, StringComparison.InvariantCultureIgnoreCase))
            .Select(pfe => new
            {
                PointerFileEntry = pfe, 
                BinaryProperty = db.BinaryProperties.Single(bp => pfe.BinaryHash == bp.Hash)
            }).AsAsyncEnumerable();

        await foreach (var x in r)
            yield return (x.PointerFileEntry, x.BinaryProperty);
    }


    //public Repository(ILoggerFactory loggerFactory, IRepositoryOptions options, Chunker chunker)
    //{
    //    var logger = loggerFactory.CreateLogger<Repository>();

    //    try
    //    {
    //        // Check the credentials with a short Retry interval
    //        var c = options.GetBlobContainerClient(new BlobClientOptions { Retry = { MaxRetries = 2 } });
    //        c.Exists();
    //        //TODO test with wrong accountname, accountkey
    //    }
    //    catch (AggregateException e)
    //    {
    //        logger.LogError(e);

    //        var msg = e.InnerExceptions.Select(ee => ee.Message).Distinct().Join();
    //        throw new ArgumentException("Cannot connect to blob container. Double check AccountName and AccountKey or network connectivity?");
    //    }

    //    /* 
    //     * RequestFailedException: The condition specified using HTTP conditional header(s) is not met.
    //     *      -- this is a throttling error most likely, hence specifiying exponential backoff
    //     *      as per https://docs.microsoft.com/en-us/azure/architecture/best-practices/retry-service-specific#blobs-queues-and-files
    //     */
    //    var bco = new BlobClientOptions()
    //    {
    //        Retry =
    //        {
    //            Delay      = TimeSpan.FromSeconds(2),  //The delay between retry attempts for a fixed approach or the delay on which to base calculations for a backoff-based approach
    //            MaxRetries = 10,                       //The maximum number of retry attempts before giving up
    //            Mode       = RetryMode.Exponential,    //The approach to use for calculating retry delays
    //            MaxDelay   = TimeSpan.FromSeconds(120) //The maximum permissible delay between retry attempts
    //        }
    //    };

    //    var container = options.GetBlobContainerClient(bco);

    //    var r0 = container.CreateIfNotExists(PublicAccessType.None);
    //    if (r0 is not null && r0.GetRawResponse().Status == (int)HttpStatusCode.Created)
    //        logger.LogInformation($"Created container {options.ContainerName}... ");

    //    Binaries           = new(loggerFactory.CreateLogger<BinaryRepository>(), this, container, chunker);
    //    Chunks             = new(loggerFactory.CreateLogger<ChunkRepository>(), this, container, options.Passphrase);
    //    PointerFileEntries = new(loggerFactory.CreateLogger<PointerFileEntryRepository>(), this);
    //    States             = new(loggerFactory.CreateLogger<StateRepository>(), this, container, options.Passphrase);
    //}

    // --------- OTHER HELPERS ---------

    private static readonly BlockBlobOpenWriteOptions ThrowOnExistOptions = new() // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
    {
        OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }
    };

    public async Task<(int binaryCount, long binariesSize, int currentPointerFileEntryCount)> GetStats()
    {
        var binaryCount                  = await CountBinariesAsync();
        var binariesSize                 = await TotalBinaryIncrementalLengthAsync();
        var currentPointerFileEntryCount = await CountPointerFileEntriesAsync();

        return (binaryCount, binariesSize, currentPointerFileEntryCount);
    }


    // --------- FINALIZER ---------
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~Repository()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            dbContextFactory.Dispose();
    }
}