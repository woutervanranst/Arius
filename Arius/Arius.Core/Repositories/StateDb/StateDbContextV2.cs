using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Repositories.StateDb;

internal record PointerFileEntryV2
{
    public BinaryHash BinaryHash   { get; init; }
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
    public BinaryHash Hash              { get; init; }
    public long       OriginalLength    { get; init; }
    public long       ArchivedLength    { get; init; }
    public long       IncrementalLength { get; init; }
    public int        ChunkCount        { get; init; }
}

internal class StateDbContextV2 : DbContext
{
    public virtual DbSet<PointerFileEntryV2> PointerFileEntries { get; set; }
    public virtual DbSet<BinaryPropertiesV2> BinaryProperties   { get; set; }

    private readonly string      dbPath;
    private readonly Action<int> hasChanges;

    ///// <summary>
    ///// Required for EF Migrations (potentially also Moq, UnitTests but not sure)
    ///// </summary>
    //public StateDbContext()
    //{
    //}
    internal StateDbContextV2(string dbPath) : this(dbPath, new Action<int>(_ => { }))
    {
    }

    internal StateDbContextV2(string dbPath, Action<int> hasChanges)
    {
        this.dbPath     = dbPath;
        this.hasChanges = hasChanges;
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

        var bme = modelBuilder.Entity<BinaryProperties>(builder =>
        {
            builder.Property(bm => bm.Hash)
                .HasColumnName("BinaryHash")
                .HasConversion(bh => bh.Value, value => new BinaryHash(value));
            //.HasConversion(new MyValueConverter());

            builder.HasKey(bm => bm.Hash);

            builder.HasIndex(bm => bm.Hash)
                .IsUnique();
        });

        var pfes = modelBuilder.Entity<PointerFileEntry>(builder =>
        {
            builder.Property(pfe => pfe.BinaryHash)
                .HasColumnName("BinaryHash")
                .HasConversion(bh => bh.Value, value => new BinaryHash(value));

            builder.HasIndex(pfe => pfe.VersionUtc); //to facilitate Versions.Distinct

            builder.HasIndex(pfe => pfe.RelativeName); //to facilitate PointerFileEntries.GroupBy(RelativeName)

            builder.HasKey(pfe => new { pfe.BinaryHash, pfe.RelativeName, pfe.VersionUtc });
        });
    }

    public override int SaveChanges()
    {
        var numChanges = base.SaveChanges();
        hasChanges(numChanges);
        return numChanges;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        var numChanges = await base.SaveChangesAsync(cancellationToken);
        hasChanges(numChanges);
        return numChanges;
    }
}