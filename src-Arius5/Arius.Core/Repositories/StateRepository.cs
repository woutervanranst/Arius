using Arius.Core.Models;
using Humanizer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

public class StateRepository
{
    private readonly ILogger<StateRepository>                     logger;
    public           FileInfo                                     StateDatabaseFile { get; }
    private readonly DbContextOptions<SqliteStateDatabaseContext> dbContextOptions;

    public StateRepository(FileInfo stateDatabaseFile, ILogger<StateRepository> logger)
    {
        this.logger       = logger;
        StateDatabaseFile = stateDatabaseFile;
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        dbContextOptions = optionsBuilder
            .UseSqlite($"Data Source={stateDatabaseFile.FullName}"/*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); })
            .Options;

        using var context = GetContext();
        //context.Database.Migrate();
        context.Database.EnsureCreated();
    }

    private SqliteStateDatabaseContext GetContext() => new(dbContextOptions, OnChanges);

    private void OnChanges(int changes) => HasChanges = HasChanges || changes > 0;
    public  bool HasChanges             { get; private set; }

    public void Vacuum()
    {
        // Flush all connections before vacuuming, releasing all connections - see https://github.com/dotnet/efcore/issues/27139#issuecomment-1007588298
        SqliteConnection.ClearAllPools();
        var originalLength = StateDatabaseFile.Length;

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
        var vacuumedLength = StateDatabaseFile.Length;

        if (originalLength != vacuumedLength)
            logger.LogInformation("Vacuumed database from {originalLength} to {vacuumedLength}, saved {savedBytes}", originalLength.Bytes().Humanize(), vacuumedLength.Bytes().Humanize(), (originalLength - vacuumedLength).Bytes().Humanize());
        else
            logger.LogInformation("Vacuumed database but no change in size");
    }

    public void Delete()
    {
        SqliteConnection.ClearAllPools();
        StateDatabaseFile.Delete();
    }

    // --- BINARYPROPERTIES

    private static readonly Func<SqliteStateDatabaseContext, Hash, BinaryPropertiesDto?> findBinaryProperty = 
        EF.CompileQuery((SqliteStateDatabaseContext dbContext, Hash h) =>
            dbContext.Set<BinaryPropertiesDto>().SingleOrDefault(x => x.Hash == h));

    internal BinaryPropertiesDto? GetBinaryProperty(Hash h)
    {
        using var context = GetContext();

        return findBinaryProperty(context, h);
    }

    internal void AddBinaryProperties(params BinaryPropertiesDto[] bps)
    {
        using var context = GetContext();

        context.BinaryProperties.AddRange(bps);
        context.SaveChanges();
    }

    // --- POINTERFILEENTRIES

    internal void UpsertPointerFileEntries(params PointerFileEntryDto[] pfes)
    {
        using var context = GetContext();

        foreach (var pfe in pfes)
        {
            var existingPfe = context.PointerFileEntries.Find(pfe.Hash, pfe.RelativeName);

            if (existingPfe is null)
            {
                context.PointerFileEntries.Add(pfe);
            }
            else
            {
                existingPfe.CreationTimeUtc  = pfe.CreationTimeUtc;
                existingPfe.LastWriteTimeUtc = pfe.LastWriteTimeUtc;
            }
        }

        context.SaveChanges();
    }

    //internal IEnumerable<PointerFileEntryDto> GetPointerFileEntries()
    //{
    //    using var context = GetContext();
    //    foreach (var pfe in context.PointerFileEntries)
    //        yield return pfe;
    //}

    internal void DeletePointerFileEntries(Func<PointerFileEntryDto, bool> shouldBeDeleted)
    {
        using var context = GetContext();

        foreach (var pfe in context.PointerFileEntries.Where(shouldBeDeleted))
            context.PointerFileEntries.Remove(pfe);

        context.SaveChanges();
    }
}
