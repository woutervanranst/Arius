using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.DbMigrationV3V5.v3;

internal class StateDbContextV3 : DbContext
{
    public virtual DbSet<PointerFileEntry> PointerFileEntries { get; set; }
    public virtual DbSet<ChunkEntry>    ChunkEntries   { get; set; }

    private readonly string dbPath;
    private readonly Action<int> hasChanges;

    internal StateDbContextV3(string dbPath) : this(dbPath, new Action<int>(_ => { }))
    {
    }
    internal StateDbContextV3(string dbPath, Action<int> hasChanges)
    {
        this.dbPath = dbPath;
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

        var cemb = modelBuilder.Entity<ChunkEntry>();
        cemb.ToTable("ChunkEntries");
        cemb.HasKey(c => c.Hash);
        cemb.HasIndex(c => c.Hash).IsUnique();

        cemb.Property(c => c.AccessTier)
            .HasConversion(new AccessTierConverter());


        var pfemb = modelBuilder.Entity<PointerFileEntry>();
        pfemb.ToTable("PointerFileEntries");
        pfemb.HasKey(pfe => new { pfe.BinaryHashValue, pfe.RelativeName, pfe.VersionUtc });
        
        pfemb.HasIndex(pfe => pfe.BinaryHashValue);  // NOT unique
        pfemb.HasIndex(pfe => pfe.VersionUtc);       //to facilitate Versions.Distinct
        pfemb.HasIndex(pfe => pfe.RelativeName);     // to facilitate GetPointerFileEntriesAtVersionAsync

        //pfemb.Property(pfe => pfe.BinaryHash)
        //    .HasConversion(new BinaryHashConverter());
        pfemb.Property(pfe => pfe.RelativeName)
            .HasConversion(new RemovePointerFileExtensionConverter());

        // PointerFileEntries * -- 1 Chunk
        pfemb.HasOne(pfe => pfe.Chunk)
            .WithMany(c => c.PointerFileEntries)
            .HasForeignKey(pfe => pfe.BinaryHashValue);
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

    public override string ToString() => dbPath;

    private class RemovePointerFileExtensionConverter : ValueConverter<string, string>
    {
        public RemovePointerFileExtensionConverter()
            : base(
                v => v.RemoveSuffix(PointerFileInfo.Extension, StringComparison.InvariantCultureIgnoreCase).ToPlatformNeutralPath(), // Convert from Model to Provider (code to db)
                v => $"{v}{PointerFileInfo.Extension}".ToPlatformSpecificPath()) // Convert from Provider to Model (db to code)
        {
        }
    }

    private class AccessTierConverter : ValueConverter<AccessTier, int>
    {
        public AccessTierConverter() : base(
            tier => ConvertTierToNumber(tier),
            number => ConvertNumberToTier(number))
        { }

        private static int ConvertTierToNumber(AccessTier tier)
        {
            if (tier == AccessTier.Archive)
                return 10;
            if (tier == AccessTier.Cold)
                return 20;
            if (tier == AccessTier.Cool)
                return 30;
            if (tier == AccessTier.Hot)
                return 40;

            return -1;
        }

        private static AccessTier ConvertNumberToTier(int number)
        {
            return number switch
            {
                10 => AccessTier.Archive,
                20 => AccessTier.Cold,
                30 => AccessTier.Cool,
                40 => AccessTier.Hot,
                _  => (AccessTier)"unknown"
            };
        }
    }

}