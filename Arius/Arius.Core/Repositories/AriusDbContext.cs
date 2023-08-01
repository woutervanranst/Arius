using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    internal interface IAriusDbContextFactory
    {
        Task LoadAsync();
        AriusDbContext GetContext();
        Task SaveAsync(DateTime versionUtc);
    }

    internal class AriusDbContextMockedFactory : IAriusDbContextFactory
    {
        private readonly AriusDbContext mockedContext;

        public AriusDbContextMockedFactory(AriusDbContext mockedContext)
        {
            this.mockedContext = mockedContext;
        }

        public Task LoadAsync() => Task.CompletedTask;
        public AriusDbContext GetContext() => mockedContext;
        public Task SaveAsync(DateTime versionUtc) => Task.CompletedTask;
    }

    internal class AriusDbContextFactory : IAriusDbContextFactory
    {
        private readonly ILogger logger;
        private readonly BlobContainerClient container;
        private readonly string passphrase;
        private readonly string localDbPath;

        public AriusDbContextFactory(ILogger logger, BlobContainerClient container, string passphrase)
        {
            this.logger = logger;
            this.container = container;
            this.passphrase = passphrase;

            localDbPath = Path.GetTempFileName(); //TODO write this to the /log directory so it is outside of the container in case of a crash
        }


        public async Task LoadAsync()
        {
            var lastStateBlobName = await container.GetBlobsAsync(prefix: $"{Repository.StateDbsFolderName}/")
                .Select(bi => bi.Name)
                .OrderBy(n => n)
                .LastOrDefaultAsync();

            if (lastStateBlobName is null)
            {
                await using var db = new AriusDbContext(localDbPath);
                await db.Database.EnsureCreatedAsync();

                logger.LogInformation($"Created new state database to '{localDbPath}'");
            }
            else
            {
                await using var ss = await container.GetBlobClient(lastStateBlobName).OpenReadAsync();
                await using var ts = File.OpenWrite(localDbPath);
                await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);

                logger.LogInformation($"Successfully downloaded latest state '{lastStateBlobName}' to '{localDbPath}'");
            }
        }

        public AriusDbContext GetContext()
        {
            if (!File.Exists(localDbPath))
                throw new InvalidOperationException("The state database file does not exist. Was it already committed?"); //TODO test?

            return new AriusDbContext(localDbPath, HasChanges);
        }

        private bool hasChanges = false;
        private void HasChanges(int numChanges)
        {
            if (numChanges > 0)
                hasChanges = true;

            logger.LogDebug($"{numChanges} state entries written to the database");
        }

        public async Task SaveAsync(DateTime versionUtc)
        {
            if (hasChanges)
            {
                logger.LogInformation("No changes made in this version, skipping upload.");
                return;
            }

            var vacuumedDbPath = Path.GetTempFileName();

            await using var db = GetContext();
            await db.Database.ExecuteSqlRawAsync($"VACUUM main INTO '{vacuumedDbPath}';"); //https://www.sqlitetutorial.net/sqlite-vacuum/

            var originalLength = FileExtensions.Length(localDbPath);
            var vacuumedlength = FileExtensions.Length(vacuumedDbPath);

            if (originalLength != vacuumedlength)
                logger.LogInformation($"Vacuumed database from {originalLength.GetBytesReadable()} to {vacuumedlength.GetBytesReadable()}");

            var blobName = $"{Repository.StateDbsFolderName}/{versionUtc:s}";
            var bbc = container.GetBlockBlobClient(blobName);
            await using (var ss = File.OpenRead(vacuumedDbPath)) //do not convert to inline using; the File.Delete will fail
            {
                await using var ts = await bbc.OpenWriteAsync(overwrite: true);
                await CryptoService.CompressAndEncryptAsync(ss, ts, passphrase);
            }

            await bbc.SetAccessTierAsync(AccessTier.Cool);
            await bbc.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = CryptoService.ContentType });

            // Move the previous states to Archive storage
            await foreach (var bi in container.GetBlobsAsync(prefix: $"{Repository.StateDbsFolderName}/")
                                        .OrderBy(bi => bi.Name)
                                        .SkipLast(2)
                                        .Where(bi => bi.Properties.AccessTier != AccessTier.Archive))
            {
                var bc = container.GetBlobClient(bi.Name);
                await bc.SetAccessTierAsync(AccessTier.Archive);
            }

            //Delete the original database
            await db.Database.EnsureDeletedAsync();

            var p = $"arius-{versionUtc.ToString("o").Replace(":", "-")}.sqlite";
            File.Move(vacuumedDbPath, p);

            logger.LogInformation($"State upload succesful into '{blobName}'");
        }
    }

    internal class AriusDbContext : DbContext
    {
        public virtual DbSet<PointerFileEntry> PointerFileEntries { get; set; }
        public virtual DbSet<BinaryProperties> BinaryProperties { get; set; }

        private readonly string      dbPath;
        private readonly Action<int> hasChanges;

        /// <summary>
        /// REQUIRED FOR MOQ / UNIT TESTING PURPOSES
        /// </summary>
        internal AriusDbContext()
        { 
        }
        internal AriusDbContext(string dbPath) : this(dbPath, new Action<int>(_ => { }))
        {
        }
        internal AriusDbContext(string dbPath, Action<int> hasChanges)
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
}