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

    //public class MyValueConverter : ValueConverter<BinaryHash, string>
    //{
    //    public MyValueConverter(ConverterMappingHints mappingHints = null)
    //        : base(
    //            bh => bh.Value,
    //            value => new BinaryHash(value),
    //            mappingHints
    //        )
    //    { }
    //}

    private class AriusDbContext : DbContext
    {
        //public DbSet<PointerFileEntry> PointerFileEntries { get; set; }
        public DbSet<BinaryMetadata> BinaryMetadata { get; set; }



        

        public static async Task<AriusDbContext> GetAriusDbContext()
        {
            var path = await DbPathTask.Task;
            var db = new AriusDbContext(path);
            await db.Database.EnsureCreatedAsync();

            return db;
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

            //bme
                //.HasKey(bm => bm.Hash);

            //bme
                //.Property(bm => bm.Hash).HasConversion(bh => bh.Value, h => new BinaryHash(h));
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

