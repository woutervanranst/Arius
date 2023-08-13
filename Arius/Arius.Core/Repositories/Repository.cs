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


    dit moet gemerged worden met AriusDbContext

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