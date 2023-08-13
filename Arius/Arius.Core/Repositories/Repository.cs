using Arius.Core.Facade;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using PostSharp.Constraints;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Arius.Core.Repositories;

internal class RepositoryBuilder
{
    private readonly ILogger<Repository> logger;

    public RepositoryBuilder(ILogger<Repository> logger)
    {
        this.logger = logger;
    }

    private IRepositoryOptions  options   = default;
    private BlobContainerClient container = default;
    public RepositoryBuilder WithOptions(IRepositoryOptions options)
    {
        this.options = options;

        // Get the Blob Container Client
        container = options.GetBlobContainerClient(GetExponentialBackoffOptions());

        return this;
    }


    private Repository.IAriusDbContextFactory dbContextFactory;
    public RepositoryBuilder WithLatestStateDatabase()
    {
        dbContextFactory = new Repository.AriusDbContextFactory(logger, container, options.Passphrase);
        
        return this;
    }

    //public RepositoryBuilder WithMockedDatabase(Repository.AriusDbContext mockedContext)
    //{
    //    dbContextFactory = new Repository.AriusDbContextMockedFactory(mockedContext);

    //    return this;
    //}

    public async Task<Repository> BuildAsync()
    {
        if (options == default(IRepositoryOptions))
            throw new ArgumentException("Options not set");
        

        // Ensure the Blob Container exists
        var r = await container.CreateIfNotExistsAsync(PublicAccessType.None);
        if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
            logger.LogInformation($"Created container {options.ContainerName}... ");

        // Initialize the DbContextFactory (ie. download the state from blob)
        await dbContextFactory.LoadAsync();

        return new Repository(logger, options, dbContextFactory, container);
    }

    private static BlobClientOptions GetExponentialBackoffOptions()
    {
        /* 
         * RequestFailedException: The condition specified using HTTP conditional header(s) is not met.
         *      -- this is a throttling error most likely, hence specifiying exponential backoff
         *      as per https://docs.microsoft.com/en-us/azure/architecture/best-practices/retry-service-specific#blobs-queues-and-files
         */
        return new BlobClientOptions()
        {
            Retry =
            {
                Delay      = TimeSpan.FromSeconds(2),  //The delay between retry attempts for a fixed approach or the delay on which to base calculations for a backoff-based approach
                MaxRetries = 10,                       //The maximum number of retry attempts before giving up
                Mode       = RetryMode.Exponential,    //The approach to use for calculating retry delays
                MaxDelay   = TimeSpan.FromSeconds(120) //The maximum permissible delay between retry attempts
            }
        };
    }
}






internal partial class Repository : IDisposable
{
    private readonly ILogger<Repository>    logger;
    private readonly IAriusDbContextFactory dbContextFactory;

    [ComponentInternal("Arius.Cli.Tests")]  // added only for Moq
    public Repository()
    {
    }

    [ComponentInternal(typeof(RepositoryBuilder))]
    public Repository(ILogger<Repository> logger, IRepositoryOptions options, IAriusDbContextFactory dbContextFactory, BlobContainerClient container)
    {
        this.logger           = logger;
        this.dbContextFactory = dbContextFactory;
        this.Options          = options;

        // !!!!!!!!!!!!!! TODO THIS NEEDS TO BE REFACTORED !!!!!!!!!!!!!
        if (DateTime.Now.Day > 13)
            throw new NotImplementedException();
        var chunker = new ByteBoundaryChunker(new SHA256Hasher(options));

        Binaries           = new(this, container, chunker);
        Chunks             = new(this, container, options.Passphrase);
        PointerFileEntries = new(this);
    }

    public IRepositoryOptions Options { get; }

    // --------- STATES ---------

    internal const string StateDbsFolderName = "states";

    private AriusDbContext GetAriusDbContext() => dbContextFactory.GetContext(); // note for testing internal - perhaps use the IAriusDbContextFactory directly?

    public async Task SaveStateToRepository(DateTime versionUtc)
    {
        await dbContextFactory.SaveAsync(versionUtc);
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
        var binaryCount = await Binaries.CountAsync();
        var binariesSize = await Binaries.TotalIncrementalLengthAsync();
        var currentPointerFileEntryCount = await PointerFileEntries.CountAsync();

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