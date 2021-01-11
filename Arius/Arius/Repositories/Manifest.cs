using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Arius.Repositories
{
    internal class Manifest : DbContext
    {
        public DbSet<ManifestEntry> Manifests { get; set; }
        //public DbSet<OrderedChunk> Chunks { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(@"Data Source=c:\arius.db");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var me = modelBuilder.Entity<ManifestEntry>();
            me.HasKey(m => m.HashValue);
            me.HasMany(m => m.Entries);
            me.HasMany(m => m.Chunks);

            var pfee = modelBuilder.Entity<PointerFileEntry>();
            pfee.HasKey(pfe => new { pfe.RelativeName, pfe.Version });

            var oce = modelBuilder.Entity<OrderedChunk>();
            oce.HasKey(oc => new { oc.ManifestHashValue, oc.ChunkHashValue });

        }
    }


    internal class ManifestEntry
    {
        public string HashValue { get; set; }
        public List<PointerFileEntry> Entries { get; init; } = new();
        public List<OrderedChunk> Chunks { get; init; } = new();
    }

    internal class PointerFileEntry
    {
        public string RelativeName { get; set; }
        public DateTime Version { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? CreationTimeUtc { get; set; }
        public DateTime? LastWriteTimeUtc { get; set; }
    }

    internal class OrderedChunk
    {
        public string ManifestHashValue { get; set; }
        public string ChunkHashValue { get; set; }
        public int Order { get; set; }
    }
}
