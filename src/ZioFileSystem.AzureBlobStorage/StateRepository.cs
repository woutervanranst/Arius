using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace ZioFileSystem.AzureBlobStorage;

public class StateRepository
{
    private readonly DbContextOptions<SqliteStateDatabaseContext> dbContextOptions;

    public StateRepository()
    {
        var stateDatabaseFile = new System.IO.FileInfo("state.db");
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

        return context.BinaryProperties.Find(h.Value);
    }

    internal void AddBinaryProperty(BinaryPropertiesDto bp)
    {
        using var context = GetContext();

        context.BinaryProperties.Add(bp);
        context.SaveChanges();
    }

    // --- POINTERFILEENTRIES

    internal void UpsertPointerFileEntry(PointerFileEntryDto pfe)
    {
        using var context = GetContext();

        var existingPfe = context.PointerFileEntries.Find(pfe.Hash, pfe.RelativeName);

        if (existingPfe is null)
        {
            context.PointerFileEntries.Add(pfe);
        }
        else
        {
            existingPfe.CreationTimeUtc = pfe.CreationTimeUtc;
            existingPfe.LastWriteTimeUtc = pfe.LastWriteTimeUtc;
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