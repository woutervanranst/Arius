using Arius.Core.Domain.Extensions;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Arius.Core.Infrastructure.Repositories;

internal record PointerFileEntryDto
{
    public         byte[]              Hash             { get; init; }
    public         string              RelativeName     { get; init; }
    public         DateTime?           CreationTimeUtc  { get; set; }
    public         DateTime?           LastWriteTimeUtc { get; set; }
    public virtual BinaryPropertiesDto BinaryProperties { get; init; }
}

internal record BinaryPropertiesDto
{
    public         byte[]                           Hash               { get; init; }
    public         long                             OriginalSize       { get; init; }
    public         long                             ArchivedSize       { get; init; }
    public         StorageTier                      StorageTier        { get; set; }
    public virtual ICollection<PointerFileEntryDto> PointerFileEntries { get; set; }
}

internal class SqliteStateDatabaseContext : DbContext
{
    private readonly Action<int> onChanges;

    public SqliteStateDatabaseContext(DbContextOptions<SqliteStateDatabaseContext> options, Action<int> onChanges)
        : base(options)
    {
        this.onChanges = onChanges;
    }

    public virtual DbSet<PointerFileEntryDto> PointerFileEntries { get; set; }
    public virtual DbSet<BinaryPropertiesDto> BinaryProperties   { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var cemb = modelBuilder.Entity<BinaryPropertiesDto>();
        cemb.ToTable("BinaryProperties");
        cemb.HasKey(c => c.Hash);
        cemb.HasIndex(c => c.Hash).IsUnique();

        cemb.Property(c => c.StorageTier)
            .HasConversion(new AccessTierConverter());


        var pfemb = modelBuilder.Entity<PointerFileEntryDto>();
        pfemb.ToTable("PointerFileEntries");
        pfemb.HasKey(pfe => new { pfe.Hash, pfe.RelativeName });

        pfemb.HasIndex(pfe => pfe.Hash);     // NOT unique
        pfemb.HasIndex(pfe => pfe.RelativeName);  // to facilitate GetPointerFileEntriesAtVersionAsync

        pfemb.Property(pfe => pfe.RelativeName)
            .HasConversion(new RemovePointerFileExtensionConverter());

        // PointerFileEntries * -- 1 Chunk
        pfemb.HasOne(pfe => pfe.BinaryProperties)
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
                v => v.RemoveSuffix(IPointerFile.Extension, StringComparison.InvariantCultureIgnoreCase).ToPlatformNeutralPath(), // Convert from Model to Provider (code to db)
                v => $"{v}{IPointerFile.Extension}".ToPlatformSpecificPath()) // Convert from Provider to Model (db to code)
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
                StorageTier.Cold    => 20,
                StorageTier.Cool    => 30,
                StorageTier.Hot     => 40,
                _                   => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown storage tier")
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
                _  => throw new ArgumentOutOfRangeException(nameof(number), number, "Unknown storage tier")
            };
        }
    }
}