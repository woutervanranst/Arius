using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.Repositories;

internal class SqliteStateDatabaseContext : DbContext
{
    private readonly Action<int> onChanges;

    public SqliteStateDatabaseContext(DbContextOptions<SqliteStateDatabaseContext> options, Action<int> onChanges)
        : base(options)
    {
        this.onChanges = onChanges;
    }

    public virtual DbSet<PointerFileEntryDto> PointerFileEntries { get; set; }
    public virtual DbSet<BinaryPropertiesDto> BinaryProperties { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var bpb = modelBuilder.Entity<BinaryPropertiesDto>();
        bpb.ToTable("BinaryProperties");
        bpb.HasKey(c => c.Hash);
        bpb.HasIndex(c => c.Hash).IsUnique();

        bpb.Property(c => c.StorageTier)
            .HasConversion(new AccessTierConverter());


        var pfeb = modelBuilder.Entity<PointerFileEntryDto>();
        pfeb.ToTable("PointerFileEntries");
        pfeb.HasKey(pfe => new { pfe.Hash, pfe.RelativeName });

        pfeb.HasIndex(pfe => pfe.Hash);     // NOT unique
        pfeb.HasIndex(pfe => pfe.RelativeName);  // to facilitate GetPointerFileEntriesAtVersionAsync

        pfeb.Property(pfe => pfe.RelativeName)
            .HasConversion(new RemovePointerFileExtensionConverter());

        // PointerFileEntries * -- 1 Chunk
        pfeb.HasOne(pfe => pfe.BinaryProperties)
            .WithMany(c => c.PointerFileEntries)
            .HasForeignKey(pfe => pfe.Hash);
    }

    public override int SaveChanges()
    {
        var numChanges = base.SaveChanges();
        onChanges(numChanges);
        return numChanges;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        var numChanges = await base.SaveChangesAsync(cancellationToken);
        onChanges(numChanges);
        return numChanges;
    }

    private class RemovePointerFileExtensionConverter : ValueConverter<string, string>
    {
        public RemovePointerFileExtensionConverter()
            : base(
                v => v.RemovePrefix("/").RemoveSuffix(PointerFile.Extension, StringComparison.InvariantCultureIgnoreCase), // Convert from Model to Provider (code to db)
                v => $"/{v}{PointerFile.Extension}") // Convert from Provider to Model (db to code)
        {
        }
    }

    private class AccessTierConverter : ValueConverter<StorageTier, int>
    {
        public AccessTierConverter() : base(
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
    public byte[] Hash { get; init; }
    public string RelativeName { get; init; }
    public DateTime? CreationTimeUtc { get; set; }
    public DateTime? LastWriteTimeUtc { get; set; }
    public virtual BinaryPropertiesDto BinaryProperties { get; init; }
}

internal record BinaryPropertiesDto
{
    public byte[] Hash { get; init; }
    public long OriginalSize { get; init; }
    public long ArchivedSize { get; init; }
    public StorageTier StorageTier { get; set; }
    public virtual ICollection<PointerFileEntryDto> PointerFileEntries { get; set; }
}