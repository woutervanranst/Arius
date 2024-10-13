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
    private readonly IRemoteStateRepository                       remoteStateRepository;
    private readonly IStateDatabaseFile                           stateDatabaseFile;
    private readonly ILogger<SqliteLocalStateRepository>          logger;

    public SqliteLocalStateRepository(
        IRemoteStateRepository remoteStateRepository,
        IStateDatabaseFile stateDatabaseFile, 
        ILogger<SqliteLocalStateRepository> logger)
    {
        this.remoteStateRepository = remoteStateRepository;
        this.stateDatabaseFile     = stateDatabaseFile;
        this.logger                = logger;

        /*  Database is locked -> Cache = shared as per https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
         *  NOTE if it still fails, try 'pragma temp_store=memory'
         *  Set command timeout to 60s to avoid concurrency errors on 'table is locked' */
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        dbContextOptions = optionsBuilder
            .UseSqlite($"Data Source={stateDatabaseFile.FullName}" /*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); })
            .Options;

        using var context = GetContext();
        context.Database.Migrate();
    }

    private SqliteStateDatabaseContext GetContext() => new(dbContextOptions, OnChanges);

    private void OnChanges(int changes) => hasChanges = hasChanges || changes > 0;
    private bool hasChanges;


    public StateVersion Version => stateDatabaseFile.Version;


    public async Task<bool> UploadAsync(CancellationToken cancellationToken = default)
    {
        // TODO UNIT TEST

        if (hasChanges)
        {
            logger.LogInformation("Changes in version {version}, uploading...", Version.Name);
            Vacuum();
            await remoteStateRepository.UploadStateDatabaseAsync(stateDatabaseFile, Version, cancellationToken);
        }
        else
        {
            logger.LogInformation("No changes in version {version}, discarding...", Version.Name);
            Discard();
        }

        return hasChanges;


        void Vacuum()
        {
            // Flush all connections before vacuuming, releasing all connections - see https://github.com/dotnet/efcore/issues/27139#issuecomment-1007588298
            SqliteConnection.ClearAllPools();
            var originalLength = stateDatabaseFile.Length;

            using (var context = GetContext())
            {
                const string sql = "VACUUM;";
                if (context.Database.ExecuteSqlRaw(sql) == 1)
                    throw new InvalidOperationException("Vacuum failed");

                if (context.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);") == 1)
                    throw new InvalidOperationException("Checkpoint failed due to active readers");
            }

            // Flush them again after vacuum, ensuring correct database file size - or the file will be Write-locked due to connection pools
            SqliteConnection.ClearAllPools();
            var vacuumedLength = stateDatabaseFile.Length;

            if (originalLength != vacuumedLength)
                logger.LogInformation("Vacuumed database from {originalLength} to {vacuumedLength}, saved {savedBytes}", originalLength.Bytes().Humanize(), vacuumedLength.Bytes().Humanize(), (originalLength - vacuumedLength).Bytes().Humanize());
            else
                logger.LogInformation("Vacuumed database but no change in size");
        }
    }

    public void Discard()
    {
        SqliteConnection.ClearAllPools();
        stateDatabaseFile.Delete();
        logger.LogInformation("Discarded version {version}", Version.Name);
    }


    // --- BINARYPROPERTIES

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
    /// Retrieves various size metrics for the archived files, categorized by whether they cover all entries or only existing ones,
    /// whether they represent original or archived sizes, and whether they include only unique entries.
    /// </summary>
    /// <returns>
    /// A tuple containing the following size metrics:
    /// 
    /// | Size Name                   | Includes Deleted Entries | Unique | OriginalSize / ArchivedSize |
    /// |-----------------------------|--------------------------|--------|-----------------------------|
    /// | AllUniqueOriginalSize       | Yes                      | Yes    | OriginalSize                |
    /// | AllUniqueArchivedSize       | Yes                      | Yes    | ArchivedSize                |
    /// | AllOriginalSize             | Yes                      | No     | OriginalSize                |
    /// | AllArchivedSize             | Yes                      | No     | ArchivedSize                |
    /// | ExistingUniqueOriginalSize  | No                       | Yes    | OriginalSize                |
    /// | ExistingUniqueArchivedSize  | No                       | Yes    | ArchivedSize                |
    /// | ExistingOriginalSize        | No                       | No     | OriginalSize                |
    /// | ExistingArchivedSize        | No                       | No     | ArchivedSize                |
    /// 
    /// </returns>
    public SizeMetrics GetSizes()
    {
        using var context = GetContext();

        var allUniqueOriginalSize = context.BinaryProperties.Distinct().Sum(bp => bp.OriginalSize);
        var allUniqueArchivedSize = context.BinaryProperties.Distinct().Sum(bp => bp.ArchivedSize);

        //var allOriginalSize = context.BinaryProperties.Sum(bp => bp.OriginalSize);
        var allOriginalSize = 0; // kunnen we niet weten
        var allArchivedSize = 0; // kunnen we niet weten  context.BinaryProperties.Sum(bp => bp.ArchivedSize);

        var existingUniqueOriginalSize = context.BinaryProperties.Where(bp => bp.PointerFileEntries.Any()).Distinct().Sum(bp => bp.OriginalSize);
        var existingUniqueArchivedSize = context.BinaryProperties.Where(bp => bp.PointerFileEntries.Any()).Distinct().Sum(bp => bp.ArchivedSize);

        //var existingOriginalSize = context.BinaryProperties.Where(bp => bp.PointerFileEntries.Any()).Sum(bp => bp.OriginalSize);
        //var existingArchivedSize = context.BinaryProperties.Where(bp => bp.PointerFileEntries.Any()).Sum(bp => bp.ArchivedSize);

        var existingOriginalSize = context.PointerFileEntries.Sum(pfe => pfe.BinaryProperties.OriginalSize);
        var existingArchivedSize = context.PointerFileEntries.Sum(pfe => pfe.BinaryProperties.ArchivedSize);

        return new(allUniqueOriginalSize, allUniqueArchivedSize/*, allOriginalSize, allArchivedSize*/, existingUniqueOriginalSize, existingUniqueArchivedSize, existingOriginalSize, existingArchivedSize);
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

    // -- POINTERFILEENTRIES

    public IEnumerable<PointerFileEntry> GetPointerFileEntries()
    {
        using var context = GetContext();
        foreach (var pfe in context.PointerFileEntries.Select(dto => dto.ToEntity()))
            yield return pfe;
    }

    public long CountPointerFileEntries()
    {
        using var context = GetContext();
        return context.PointerFileEntries.LongCount();
    }

    public UpsertResult UpsertPointerFileEntry(PointerFileEntry pfe)
    {
        using var context = GetContext();

        var existingPfe = context.PointerFileEntries.Find(pfe.Hash.Value, pfe.RelativeName);
        var pfeDto      = pfe.ToDto();

        if (existingPfe == null)
        {
            context.PointerFileEntries.Add(pfeDto);
            context.SaveChanges();
            return UpsertResult.Added;
        }
        else
        {
            existingPfe.CreationTimeUtc  = pfeDto.CreationTimeUtc;
            existingPfe.LastWriteTimeUtc = pfeDto.LastWriteTimeUtc;

#if DEBUG
            var unmappedProperties = pfeDto.GetType().GetProperties().Select(p => p.Name)
                .Except([nameof(pfeDto.Hash), nameof(pfeDto.RelativeName), nameof(pfeDto.CreationTimeUtc), nameof(pfeDto.LastWriteTimeUtc), nameof(pfeDto.BinaryProperties)]);
            if (unmappedProperties.Any())
                throw new NotImplementedException("Not all properties are updated");
#endif
        }

        return context.SaveChanges() > 0 ? UpsertResult.Updated : UpsertResult.Unchanged;
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