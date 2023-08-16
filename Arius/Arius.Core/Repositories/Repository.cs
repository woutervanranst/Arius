using Arius.Core.Facade;
using Arius.Core.Models;
using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PostSharp.Constraints;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository : IDisposable
{
    private readonly ILogger<Repository> logger;
    private readonly BlobContainer      container;

    [ComponentInternal("Arius.Cli.Tests")] // added only for Moq
    public Repository()
    {
    }

    [ComponentInternal(typeof(RepositoryBuilder))]
    public Repository(ILogger<Repository> logger, IRepositoryOptions options, RepositoryBuilder.IStateDbContextFactory dbContextFactory, BlobContainer container)
    {
        this.logger           = logger;
        this.dbContextFactory = dbContextFactory;
        this.container        = container;
        this.Options          = options;
    }

    public IRepositoryOptions Options { get; }

    // --------- BLA ---------

    public async IAsyncEnumerable<(PointerFileEntry PointerFileEntry, BinaryProperties BinaryProperties)> GetPointerFileEntriesWithBinaryPropertiesAsync(string relativeNamePrefix)
    {
        throw new NotImplementedException();

        // TODO: use db.PointerFileEntries.Include(e => e.BinaryProperties)
        // EF Core Migrations

        await using var db = GetStateDbContext();

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