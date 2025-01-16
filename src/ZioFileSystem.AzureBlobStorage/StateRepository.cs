using Microsoft.EntityFrameworkCore;

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

    internal BinaryPropertiesDto? GetBinaryProperty(Hash h)
    {
        using var db = GetContext();

        return db.BinaryProperties.Find(h.Value);
    }

    internal void AddBinaryProperty(BinaryPropertiesDto bp)
    {
        using var db = GetContext();

        db.BinaryProperties.Add(bp);
        db.SaveChanges();
    }

    internal void UpsertPointerFileEntry(PointerFileEntryDto pfe)
    {
        using var db = GetContext();

        var existingPfe = db.PointerFileEntries.Find(pfe.Hash, pfe.RelativeName);

        if (existingPfe is null)
        {
            db.PointerFileEntries.Add(pfe);
        }
        else
        {
            existingPfe.CreationTimeUtc = pfe.CreationTimeUtc;
            existingPfe.LastWriteTimeUtc = pfe.LastWriteTimeUtc;
        }

        db.SaveChanges();
    }
}