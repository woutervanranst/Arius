using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    private class AriusDbContext : DbContext
    {
        public DbSet<PointerFileEntry> PointerFileEntries { get; set; }
        public DbSet<BinaryMetadata> BinaryMetadata { get; set; }


        public static async Task<AriusDbContext> GetAriusDbContext()
        {
            var path = await DbPathTask.Task;
            var db = new AriusDbContext(path);

            return db;
        }
        public static async Task EnsureCreated(string path)
        {
            var db = new AriusDbContext(path);
            await db.Database.EnsureCreatedAsync();
        }
        public static TaskCompletionSource<string> DbPathTask { get; } = new();

        private AriusDbContext(string dbPath)
        {
            // thread safe? https://www.sqlite.org/threadsafe.html
            this.dbPath = dbPath;
        }
        private readonly string dbPath;


        // The following configures EF to create a Sqlite database file in the
        // special "local" folder for your platform.
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={dbPath}",
                builder =>
                {
                });

        //protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        //{
        //    configurationBuilder.Properties<BinaryHash>().HaveConversion<string>();
        //}

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var bme = modelBuilder.Entity<BinaryMetadata>(builder =>
            {
                builder.Property(bm => bm.Hash)
                    .HasColumnName("BinaryHash")
                    .HasConversion(bh => bh.Value, value => new BinaryHash(value));
                    //.HasConversion(new MyValueConverter());

                builder.HasKey(bm => bm.Hash);

            });

            var pfee = modelBuilder.Entity<PointerFileEntry>(builder =>
            {
                builder.Property(pfe => pfe.BinaryHash)
                    .HasColumnName("BinaryHash")
                    .HasConversion(bh => bh.Value, value => new BinaryHash(value));

                builder.HasIndex(pfe => pfe.VersionUtc);

                builder.HasKey(pfe => new { pfe.BinaryHash, pfe.RelativeName, pfe.VersionUtc });
            });
        }

        // BinaryManifest --> in blob (potentially too big)
        // BinaryMetadata
        // PointerFileEntries

        //public async static Task GetLatestDb()
        //{
        //    var folder = Environment.SpecialFolder.LocalApplicationData;
        //    var path = Environment.GetFolderPath(folder);
        //    DbPath = $"{path}{System.IO.Path.DirectorySeparatorChar}blogging.db";

        //}

    }

}

