using Microsoft.EntityFrameworkCore;

namespace Arius.Core.Repositories.StateDb;

internal record PointerFileEntryV2
{
    public string BinaryHash   { get; init; }
    public string     RelativeName { get; init; }

    /// <summary>
    /// Version (in Universal Time)
    /// </summary>
    public DateTime VersionUtc        { get; init; }
    public bool      IsDeleted        { get; init; }
    public DateTime? CreationTimeUtc  { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }
}

internal record BinaryPropertiesV2
{
    public string Hash              { get; init; }
    public long   OriginalLength    { get; init; }
    public long   ArchivedLength    { get; init; }
    public long   IncrementalLength { get; init; }
    public int    ChunkCount        { get; init; }
}

internal class StateDbContextV2 : DbContext
{
    public virtual DbSet<PointerFileEntryV2> PointerFileEntries { get; set; }
    public virtual DbSet<BinaryPropertiesV2> BinaryProperties   { get; set; }

    private readonly string      dbPath;

    internal StateDbContextV2(string dbPath) : this(dbPath, new Action<int>(_ => { }))
    {
    }

    internal StateDbContextV2(string dbPath, Action<int> hasChanges)
    {
        this.dbPath     = dbPath;
    }


    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        /* Database is locked -> Cache = shared as per https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
         *  NOTE if it still fails, try 'pragma temp_store=memory'
         */
        options.UseSqlite($"Data Source={dbPath};Cache=Shared",
            sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(60); //set command timeout to 60s to avoid concurrency errors on 'table is locked'
            });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var bme = modelBuilder.Entity<BinaryPropertiesV2>(builder =>
        {
            builder.Property(bm => bm.Hash)
                .HasColumnName("BinaryHash");

            builder.HasKey(bm => bm.Hash);

            builder.HasIndex(bm => bm.Hash)
                .IsUnique();
        });

        var pfes = modelBuilder.Entity<PointerFileEntryV2>(builder =>
        {
            builder.Property(pfe => pfe.BinaryHash)
                .HasColumnName("BinaryHash");

            builder.HasIndex(pfe => pfe.VersionUtc); //to facilitate Versions.Distinct

            builder.HasIndex(pfe => pfe.RelativeName); //to facilitate PointerFileEntries.GroupBy(RelativeName)

            builder.HasKey(pfe => new { pfe.BinaryHash, pfe.RelativeName, pfe.VersionUtc });
        });
    }
}