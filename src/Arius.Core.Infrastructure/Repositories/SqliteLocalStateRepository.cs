using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Humanizer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Arius.Core.Infrastructure.Repositories;

internal class SqliteLocalStateRepository : ILocalStateRepository
{
    private readonly DbContextOptions<SqliteStateDatabaseContext> dbContextOptions;
    private readonly ILogger<SqliteLocalStateRepository>          logger;

    public SqliteLocalStateRepository(IStateDatabaseFile stateDatabaseFile, RepositoryVersion version, ILogger<SqliteLocalStateRepository> logger)
    {
        /*  Database is locked -> Cache = shared as per https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
         *  NOTE if it still fails, try 'pragma temp_store=memory'
         *  Set command timeout to 60s to avoid concurrency errors on 'table is locked' */
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        dbContextOptions = optionsBuilder
            .UseSqlite($"Data Source={stateDatabaseFile.FullName}" /*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); })
            .Options;

        Version           = version;
        StateDatabaseFile = stateDatabaseFile;
        this.logger       = logger;

        using var context = GetContext();
        context.Database.Migrate();
    }

    private SqliteStateDatabaseContext GetContext() => new(dbContextOptions, OnChanges);

    private void OnChanges(int changes) => HasChanges = HasChanges || changes > 0;

    public IStateDatabaseFile StateDatabaseFile { get; }
    public RepositoryVersion  Version           { get; }
    public bool               HasChanges        { get; private set; }

    public void Vacuum()
    {
        // Flush all connections before vacuuming, ensuring correct database file size
        SqliteConnection.ClearAllPools(); // https://github.com/dotnet/efcore/issues/27139#issuecomment-1007588298
        var originalLength = StateDatabaseFile.Length;

        using (var context = GetContext())
        {
            var sql = "VACUUM;";
            context.Database.ExecuteSqlRaw(sql);

            if (context.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);") == 1)
                throw new InvalidOperationException("Checkpoint failed due to active readers");
        }

        // Flush them again after vacuum, ensuring correct database file size - or the file will be Write-locked due to connection pools
        SqliteConnection.ClearAllPools(); // https://github.com/dotnet/efcore/issues/27139#issuecomment-1007588298
        var vacuumedLength = StateDatabaseFile.Length;

        if (originalLength != vacuumedLength)
            logger.LogInformation($"Vacuumed database from {originalLength.Bytes().Humanize()} to {vacuumedLength.Bytes().Humanize()}, saved {(originalLength - vacuumedLength).Bytes().Humanize()}");
        else
            logger.LogInformation($"Vacuumed database but no change in size");
    }


    public long CountPointerFileEntries()
    {
        using var context = GetContext();
        return context.PointerFileEntries.LongCount();
    }

    public IEnumerable<PointerFileEntry> GetPointerFileEntries()
    {
        using var context = GetContext();
        foreach (var pfe in context.PointerFileEntries.Select(dto => dto.ToEntity()))
            yield return pfe;
    }

    public IEnumerable<BinaryProperties> GetBinaryProperties()
    {
        using var context = GetContext();
        foreach (var bp in context.BinaryProperties.Select(dto => dto.ToEntity()))
            yield return bp;
    }

    public long CountBinaryProperties()
    {
        using var context = GetContext();
        return context.BinaryProperties.LongCount();
    }

    /// <summary>
    /// The sum of the compressed size of the net-new files (TODO SAME AS GetIncrementalSize ??)
    /// </summary>
    /// <returns></returns>
    public long GetArchiveSize()
    {
        using var context = GetContext();
        return context.BinaryProperties.Sum(bp => bp.ArchivedLength);
    }

    /// <summary>
    /// The sum of the uncompressed size of the original files
    /// </summary>
    /// <returns></returns>
    public long GetOriginalArchiveSize()
    {
        using var context = GetContext();
        return context.BinaryProperties.Sum(bp => bp.OriginalLength);
    }

    /// <summary>
    /// The sum of the compressed size of the net-new files (TODO SAME AS GetArchiveSize ??)
    /// </summary>
    /// <returns></returns>
    public long GetIncrementalSize() 
    {
        using var context = GetContext();
        return context.BinaryProperties.Sum(bp => bp.IncrementalLength);
    }

    //public IEnumerable<BinaryProperties> GetBinaryProperties()
    //{
    //    using var context = new SqliteStateDatabaseContext(dbContextOptions);
    //    foreach (var bp in context.BinaryProperties.Select(dto => dto.ToEntity()))
    //        yield return bp;
    //}

    public void AddBinary(BinaryProperties bp)
    {
        using var context = GetContext();
        context.BinaryProperties.Add(bp.ToDto());
        context.SaveChanges();
    }

    public bool BinaryExists(Hash binaryFileHash)
    {
        using var context = GetContext();
        return context.BinaryProperties.Any(bp => bp.Hash == binaryFileHash.Value);
    }

    public void UpdateBinaryStorageTier(Hash hash, StorageTier effectiveTier)
    {
        using var context = GetContext();

        var dto = context.BinaryProperties.Find(hash) 
                  ?? throw new InvalidOperationException($"Could not find BinaryProperties with hash {hash}");

        dto.StorageTier = effectiveTier;
        context.SaveChanges();
    }

    public void AddPointerFileEntry(PointerFileEntry pfe)
    {
        using var context = GetContext();
        context.PointerFileEntries.Add(pfe.ToDto());
        context.SaveChanges();
    }

    public void DeletePointerFileEntry(PointerFileEntry pfe)
    {
        using var context = GetContext();

        var dto = context.PointerFileEntries.Find(pfe.Hash.Value, pfe.RelativeName) 
                  ?? throw new InvalidOperationException($"Could not find PointerFileEntry with hash {pfe.Hash} and {pfe.RelativeName}");

        context.PointerFileEntries.Remove(dto);
        context.SaveChanges();
    }
}