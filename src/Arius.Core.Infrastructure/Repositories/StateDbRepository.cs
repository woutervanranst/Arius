using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Microsoft.EntityFrameworkCore;

namespace Arius.Core.Infrastructure.Repositories;

internal class StateDbRepository : IStateDbRepository
{
    private readonly DbContextOptions<SqliteStateDbContext> dbContextOptions;

    public StateDbRepository(DbContextOptions<SqliteStateDbContext> dbContextOptions, RepositoryVersion version)
    {
        Version               = version;
        this.dbContextOptions = dbContextOptions;

        using var context = new SqliteStateDbContext(dbContextOptions);
        //context.Database.EnsureCreated();
        context.Database.Migrate();
    }

    public RepositoryVersion Version { get; }

    public long CountPointerFileEntries()
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        return context.PointerFileEntries.LongCount();
    }

    public IEnumerable<PointerFileEntry> GetPointerFileEntries()
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        foreach (var pfe in context.PointerFileEntries.Select(dto => dto.ToEntity()))
            yield return pfe;
    }

    public Task SaveChangesAsync()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<BinaryProperties> GetBinaryProperties()
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        foreach (var bp in context.BinaryProperties.Select(dto => dto.ToEntity()))
            yield return bp;
    }

    public long CountBinaryProperties()
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        return context.BinaryProperties.LongCount();
    }

    public long GetArchiveSize()
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
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
        using var context = new SqliteStateDbContext(dbContextOptions);
        context.BinaryProperties.Add(bp.ToDto());
        context.SaveChanges();
    }

    public bool BinaryExists(Hash binaryFileHash)
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        return context.BinaryProperties.Any(bp => bp.Hash == binaryFileHash.Value);
    }

    public void UpdateBinaryStorageTier(Hash hash, StorageTier effectiveTier)
    {
        using var context = new SqliteStateDbContext(dbContextOptions);

        var dto = context.BinaryProperties.Find(hash) 
                  ?? throw new InvalidOperationException($"Could not find BinaryProperties with hash {hash}");

        dto.StorageTier = effectiveTier;
        context.SaveChanges();
    }

    public void AddPointerFileEntry(PointerFileEntry pfe)
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        context.PointerFileEntries.Add(pfe.ToDto());
        context.SaveChanges();
    }

    public void DeletePointerFileEntry(PointerFileEntry pfe)
    {
        using var context = new SqliteStateDbContext(dbContextOptions);

        var dto = context.PointerFileEntries.Find(pfe.Hash.Value, pfe.RelativeName) 
                  ?? throw new InvalidOperationException($"Could not find PointerFileEntry with hash {pfe.Hash} and {pfe.RelativeName}");

        context.PointerFileEntries.Remove(dto);
        context.SaveChanges();
    }
}