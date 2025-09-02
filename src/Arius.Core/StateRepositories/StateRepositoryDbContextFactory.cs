using Humanizer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.Core.StateRepositories;

internal class StateRepositoryDbContextFactory
{
    private readonly DbContextOptions<StateRepositoryDbContext> dbContextOptions;
    private readonly ILogger<StateRepositoryDbContextFactory>   logger;
    
    public FileInfo StateDatabaseFile { get; }
    public bool     HasChanges        { get; private set; }

    public StateRepositoryDbContextFactory(FileInfo stateDatabaseFile, bool ensureCreated, ILogger<StateRepositoryDbContextFactory> logger)
    {
        this.logger       = logger;
        StateDatabaseFile = stateDatabaseFile;
        
        var optionsBuilder = new DbContextOptionsBuilder<StateRepositoryDbContext>();
        dbContextOptions = optionsBuilder
            .UseSqlite($"Data Source={stateDatabaseFile.FullName}"/*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); })
            .Options;

        //context.Database.Migrate();

        if (ensureCreated)
        {
            using var context = CreateContext();
            context.Database.EnsureCreated();
        }
    }

    public StateRepositoryDbContext CreateContext() => new(dbContextOptions, OnChanges);

    private void OnChanges(int changes) => HasChanges = HasChanges || changes > 0;

    public void SetHasChanges() => HasChanges = true;

    public void Vacuum()
    {
        // Flush all connections before vacuuming, releasing all connections - see https://github.com/dotnet/efcore/issues/27139#issuecomment-1007588298
        SqliteConnection.ClearAllPools();
        var originalLength = StateDatabaseFile.Length;

        using (var context = CreateContext())
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
            logger.LogInformation("Vacuumed database from {originalLength} to {vacuumedLength}, saved {savedBytes}",
                originalLength.Bytes().Humanize(), vacuumedLength.Bytes().Humanize(), (originalLength - vacuumedLength).Bytes().Humanize());
        else
            logger.LogInformation("Vacuumed database but no change in size");
    }

    public void Delete()
    {
        SqliteConnection.ClearAllPools();
        StateDatabaseFile.Delete();
    }
}