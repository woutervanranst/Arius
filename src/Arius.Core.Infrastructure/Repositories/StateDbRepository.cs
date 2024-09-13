using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Infrastructure.Extensions;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace Arius.Core.Infrastructure.Repositories;

internal class StateDbRepository : IStateDbRepository
{
    private readonly DbContextOptions<SqliteStateDbContext> dbContextOptions;
    private readonly ILogger<StateDbRepository>             logger;

    public StateDbRepository(DbContextOptions<SqliteStateDbContext> dbContextOptions, RepositoryVersion version, ILogger<StateDbRepository> logger)
    {
        Version               = version;
        this.dbContextOptions = dbContextOptions;
        this.logger           = logger;

        using var context = new SqliteStateDbContext(dbContextOptions, _ => { });
        //context.Database.EnsureCreated();
        context.Database.Migrate();
    }

    private SqliteStateDbContext GetContext() => new(dbContextOptions, OnChanges);

    private void OnChanges(int changes) => HasChanges = HasChanges || changes > 0;

    public RepositoryVersion Version    { get; }
    public bool              HasChanges { get; private set; }

    public void Vacuum()
    {
        var originalDbPath = dbContextOptions.GetDatabasePath();

        var originalLength = new FileInfo(originalDbPath).Length;

        using (var context = GetContext())
        {
            var sql = "VACUUM;";
            context.Database.ExecuteSqlRaw(sql);
        }

        var vacuumedLength = new FileInfo(originalDbPath).Length;

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

    public long GetArchiveSize()
    {
        using var context = GetContext();
        return context.BinaryProperties.Sum(bp => bp.ArchivedLength);
    }

    //public IEnumerable<BinaryProperties> GetBinaryProperties()
    //{
    //    using var context = new SqliteStateDbContext(dbContextOptions);
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