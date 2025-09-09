using Humanizer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Zio;

namespace Arius.Core.Shared.StateRepositories;

internal class StateRepositoryDbContextPool
{
    private readonly PooledDbContextFactory<StateRepositoryDbContext> factory;
    private readonly ILogger<StateRepositoryDbContextPool> logger;

    private bool hasChanges;

    public FileEntry StateDatabaseFile { get; }
    public bool HasChanges => Volatile.Read(ref hasChanges);

    public StateRepositoryDbContextPool(FileEntry stateDatabaseFile, bool ensureCreated, ILogger<StateRepositoryDbContextPool> logger)
    {
        this.logger = logger;
        StateDatabaseFile = stateDatabaseFile;

        var  interceptor = new AnyChangesInterceptor(SetHasChanges);

        var internalName = stateDatabaseFile.FileSystem.ConvertPathToInternal(stateDatabaseFile.Path);

        var options = new DbContextOptionsBuilder<StateRepositoryDbContext>()
            .UseSqlite($"Data Source={internalName}"/*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); })
            .AddInterceptors(interceptor)
            .Options;

        factory = new PooledDbContextFactory<StateRepositoryDbContext>(options, poolSize: 32);

        //context.Database.Migrate();

        if (ensureCreated)
        {
            using var context = CreateContext();
            context.Database.EnsureCreated();
        }
    }

    public StateRepositoryDbContext CreateContext() => factory.CreateDbContext();

    public void SetHasChanges() => Interlocked.Exchange(ref hasChanges, true);

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


    private class AnyChangesInterceptor : SaveChangesInterceptor
    {
        private readonly Action onWrites;
        public AnyChangesInterceptor(Action onWrites) => this.onWrites = onWrites;

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (result > 0) onWrites();
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (result > 0) onWrites();
            return ValueTask.FromResult(result);
        }
    }
}