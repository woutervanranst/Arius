using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Arius.Core.Repositories;

public class StateRepository
{
    private readonly DbContextOptions<SqliteStateDatabaseContext> dbContextOptions;

    public StateRepository()
    {
        var stateDatabaseFile = new FileInfo("state.db");
        //stateDatabaseFile.Delete();

        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        dbContextOptions = optionsBuilder
            .UseSqlite($"Data Source={stateDatabaseFile.FullName}" /*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); })
            .Options;

        using var context = GetContext();
        //context.Database.Migrate();
        context.Database.EnsureCreated();
    }

    private SqliteStateDatabaseContext GetContext() => new(dbContextOptions, OnChanges);

    private void OnChanges(int changes) => hasChanges = hasChanges || changes > 0;
    private bool hasChanges;

    // --- BINARYPROPERTIES

    internal BinaryPropertiesDto? GetBinaryProperty(Hash h)
    {
        using var context = GetContext();

        return context.BinaryProperties.Find(h);
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