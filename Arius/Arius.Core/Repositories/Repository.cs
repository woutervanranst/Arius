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
using Arius.Core.Repositories.BlobRepository;
using Arius.Core.Repositories.StateDb;

namespace Arius.Core.Repositories;

internal partial class Repository : IDisposable
{
    private readonly ILogger<Repository> logger;
    private readonly BlobContainer       container;

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

    // --------- STATE DB ---------

    private readonly RepositoryBuilder.IStateDbContextFactory dbContextFactory;

    private StateDbContext GetStateDbContext() => dbContextFactory.GetContext(); // note for testing internal - perhaps use the IAriusDbContextFactory directly?

    public async Task SaveStateToRepositoryAsync(DateTime versionUtc) => await dbContextFactory.SaveAsync(versionUtc);

    // --------- BLA ---------

    public async Task<(PointerFileEntry PointerFileEntry, ChunkEntry BinaryProperties)[]> GetPointerFileEntriesWithBinaryPropertiesAsync(string relativeNamePrefix)
    {
        throw new NotImplementedException();

        // TODO: use db.PointerFileEntries.Include(e => e.BinaryProperties)
        // EF Core Migrations

        //await using var db = GetStateDbContext();

        //var r = db.PointerFileEntries.Where(pfe => pfe.RelativeName.StartsWith(relativeNamePrefix, StringComparison.InvariantCultureIgnoreCase))
        //    .Select(pfe => new
        //    {
        //        PointerFileEntry = pfe, 
        //        BinaryProperty = db.BinaryProperties.Single(bp => pfe.BinaryHash == bp.Hash)
        //    }).AsAsyncEnumerable();

        //await foreach (var x in r)
        //    yield return (x.PointerFileEntry, x.BinaryProperty);
    }


    // --------- OTHER HELPERS ---------

    public async Task<(int binaryCount, long chunkSize, int currentPointerFileEntryCount)> GetStats()
    {
        var binaryCount                  = await CountBinariesAsync();
        var chunkSize                    = await TotalChunkIncrementalLengthAsync();
        var currentPointerFileEntryCount = await CountPointerFileEntriesAsync();

        return (binaryCount, chunkSize, currentPointerFileEntryCount);
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