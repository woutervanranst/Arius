using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.Repositories;

internal class SqliteStateRepositoryContext : DbContext
{
    private readonly Action<int> onChanges;

    public SqliteStateRepositoryContext(DbContextOptions<SqliteStateRepositoryContext> options, Action<int> onChanges)
        : base(options)
    {
        this.onChanges = onChanges;
    }

    public virtual DbSet<PointerFileEntryDto> PointerFileEntries { get; set; }
    public virtual DbSet<BinaryPropertiesDto> BinaryProperties { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<Hash>()
            .HaveConversion<HashToByteConverter>();
        configurationBuilder.Properties<StorageTier>()
            .HaveConversion<StorageTierConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var bpb = modelBuilder.Entity<BinaryPropertiesDto>();
        bpb.ToTable("BinaryProperties");
        bpb.HasKey(bp => bp.Hash);
        bpb.HasIndex(bp => bp.Hash).IsUnique();

        //bpb.Property(bp => bp.Hash)
        //    .HasConversion(new HashToByteConverter());
        //bpb.Property(bp => bp.ParentHash)
        //    .HasConversion(new HashToByteConverter());
        //bpb.Property(bp => bp.StorageTier)
        //    .HasConversion(new StorageTierConverter());


        var pfeb = modelBuilder.Entity<PointerFileEntryDto>();
        pfeb.ToTable("PointerFileEntries");
        pfeb.HasKey(pfe => new { pfe.Hash, pfe.RelativeName });

        pfeb.HasIndex(pfe => pfe.Hash);     // NOT unique
        pfeb.HasIndex(pfe => pfe.RelativeName);  // to facilitate GetPointerFileEntriesAtVersionAsync

        //pfeb.Property(pfe => pfe.Hash)
        //    .HasConversion(new HashToByteConverter());
        pfeb.Property(pfe => pfe.RelativeName)
            .HasConversion(new RemovePointerFileExtensionConverter());

        // PointerFileEntries * -- 1 Chunk
        pfeb.HasOne(pfe => pfe.BinaryProperties)
            .WithMany(c => c.PointerFileEntries)
            .HasForeignKey(pfe => pfe.Hash);

        //builder.Property(e => e.Hash)
        //    .Metadata
        //    .SetValueComparer(new ValueComparer<byte[]>(
        //        (obj, otherObj) => ReferenceEquals(obj, otherObj),
        //        obj => obj.GetHashCode(),
        //        obj => obj));
    }

    public override int SaveChanges()
    {
        var numChanges = base.SaveChanges();
        onChanges(numChanges);
        return numChanges;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var numChanges = await base.SaveChangesAsync(cancellationToken);
        onChanges(numChanges);
        return numChanges;
    }

    private class RemovePointerFileExtensionConverter : ValueConverter<string, string>
    {
        public RemovePointerFileExtensionConverter()
            : base(
                v => v.RemovePrefix('/').RemoveSuffix(PointerFile.Extension, StringComparison.InvariantCultureIgnoreCase), // Convert from Model to Provider (code to db)
                v => $"/{v}{PointerFile.Extension}" // Convert from Provider to Model (db to code)
            ) { }
    }

    private class HashToByteConverter : ValueConverter<Hash, byte[]>
    {
        public HashToByteConverter()
            : base(
                v => (byte[])v, // Convert from Model to Provider (code to db)
                v => (Hash)v // Convert from Provider to Model (db to code)
            ) { }
    }

    private class StorageTierConverter : ValueConverter<StorageTier, int>
    {
        public StorageTierConverter() : base(
            tier => ConvertTierToNumber(tier),
            number => ConvertNumberToTier(number))
        { }

        private static int ConvertTierToNumber(StorageTier tier)
        {
            return tier switch
            {
                StorageTier.Archive => 10,
                StorageTier.Cold => 20,
                StorageTier.Cool => 30,
                StorageTier.Hot => 40,
                _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown storage tier")
            };
        }

        private static StorageTier ConvertNumberToTier(int number)
        {
            return number switch
            {
                10 => StorageTier.Archive,
                20 => StorageTier.Cold,
                30 => StorageTier.Cool,
                40 => StorageTier.Hot,
                _ => throw new ArgumentOutOfRangeException(nameof(number), number, "Unknown storage tier")
            };
        }
    }
}

internal record PointerFileEntryDto
{
    public Hash Hash { get; init; }
    public string RelativeName { get; init; }
    public DateTime? CreationTimeUtc { get; set; }
    public DateTime? LastWriteTimeUtc { get; set; }
    public virtual BinaryPropertiesDto BinaryProperties { get; init; }
}

internal record BinaryPropertiesDto
{
    public         Hash                             Hash               { get; init; }
    public         Hash?                            ParentHash         { get; init; }
    public         long                             OriginalSize       { get; init; }
    public         long?                            ArchivedSize       { get; init; } // null in case of tarred archives
    public         StorageTier?                     StorageTier        { get; set; } // settable in case of tarred archives
    public virtual ICollection<PointerFileEntryDto> PointerFileEntries { get; set; }
}