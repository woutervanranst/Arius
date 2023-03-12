using System;
using System.Threading;
using System.Threading.Tasks;
using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    internal class AriusDbContext : DbContext
    {
        public virtual DbSet<PointerFileEntry> PointerFileEntries { get; set; }
        public virtual DbSet<BinaryProperties> BinaryProperties { get; set; }

        /// <summary>
        /// ONLY FOR MOCKING/UNIT TESTING PURPOSES
        /// </summary>
        internal AriusDbContext()
        { 
        }
        internal AriusDbContext(string dbPath, Action<int> hasChanges)
        {
            this.dbPath = dbPath;
            this.hasChanges = hasChanges;
        }
        private readonly string dbPath;
        private readonly Action<int> hasChanges;


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

            var pfee = modelBuilder.Entity<PointerFileEntry>(builder =>
            {
                builder.Property(pfe => pfe.BinaryHash)
                    .HasColumnName("BinaryHash")
                    .HasConversion(bh => bh.Value, value => new BinaryHash(value));

                builder.HasIndex(pfe => pfe.VersionUtc); //to facilitate Versions.Distinct

                builder.HasIndex(pfe => pfe.RelativeName); //to facilitate PointerFileEntries.GroupBy(RelativeName)

                builder.HasKey(pfe => new { pfe.BinaryHash, pfe.RelativeName, pfe.VersionUtc });

                //builder.HasOne<BinaryMetadata>(pfe => pfe.)
            });
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
        { 
            var numChanges = await base.SaveChangesAsync(cancellationToken);
            hasChanges(numChanges);
            return numChanges;
        }
    }
}