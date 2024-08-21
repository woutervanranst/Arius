using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Repositories.BlobRepository;
using Arius.Core.Repositories.StateDb;

namespace Arius.Core.Repositories;

internal partial class Repository : IDisposable
{
    private readonly ILogger<Repository> logger;
    private readonly BlobContainer       container;

    public Repository() // added only for Moq
    {
    }

    public Repository(ILogger<Repository> logger, RepositoryOptions options, RepositoryBuilder.IStateDbContextFactory dbContextFactory, BlobContainer container) // [ComponentInternal(typeof(RepositoryBuilder))]
    {
        this.logger           = logger;
        this.dbContextFactory = dbContextFactory;
        this.container        = container;
        this.Options          = options;
    }

    public RepositoryOptions Options { get; }

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

    public async Task<(int BinaryCount, long ChunkSize, int CurrentPointerFileEntryCount, int ChunkCount)> GetStatisticsAsync()
    {
        var binaryCount                  = await CountBinariesAsync();
        var chunkSize                    = await TotalChunkIncrementalLengthAsync();
        var currentPointerFileEntryCount = await CountCurrentPointerFileEntriesAsync();
        var chunkCount                   = await CountChunkEntriesAsync();

        return (binaryCount, chunkSize, currentPointerFileEntryCount, chunkCount);
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