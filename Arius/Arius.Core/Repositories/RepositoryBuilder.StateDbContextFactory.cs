using Arius.Core.Extensions;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Repositories.BlobRepository;
using Microsoft.Data.Sqlite;

namespace Arius.Core.Repositories;

internal partial class RepositoryBuilder
{
    internal interface IStateDbContextFactory : IDisposable
    {
        Task                      LoadAsync();
        Repository.StateDbContext GetContext();
        Task                      SaveAsync(DateTime versionUtc);
    }

    //internal class AriusDbContextMockedFactory : IAriusDbContextFactory
    //{
    //    private readonly AriusDbContext mockedContext;

    //    public AriusDbContextMockedFactory(AriusDbContext mockedContext)
    //    {
    //        this.mockedContext = mockedContext;
    //    }

    //    public Task LoadAsync() => Task.CompletedTask;
    //    public AriusDbContext GetContext() => mockedContext;
    //    public Task SaveAsync(DateTime versionUtc) => Task.CompletedTask;
    //}

    private sealed class StateDbContextFactory : IStateDbContextFactory
    {
        private readonly ILogger       logger;
        private readonly BlobContainer container;
        private readonly string        passphrase;

        private readonly string localDbPath;

        public StateDbContextFactory(ILogger logger, BlobContainer container, string passphrase)
        {
            this.logger     = logger;
            this.container  = container;
            this.passphrase = passphrase;

            localDbPath = Path.GetTempFileName(); //TODO write this to the /log directory so it is outside of the container in case of a crash
        }

        public async Task LoadAsync()
        {
            var lastStateBlobEntry = await container.States.GetBlobEntriesAsync()
                .OrderBy(b => b.FullName)
                .LastOrDefaultAsync();

            if (lastStateBlobEntry is null)
            {
                // Create new DB
                await using var db = new Repository.StateDbContext(localDbPath);
                await db.Database.EnsureCreatedAsync();

                logger.LogInformation($"Created new state database to '{localDbPath}'");
            }
            else
            {
                // Load existing DB
                var lastStateBlob = await container.States.GetBlobAsync(lastStateBlobEntry);
                await using (var ss = await lastStateBlob.OpenReadAsync())
                {
                    await using var ts = new FileStream(localDbPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 4096); //File.OpenWrite(localDbPath); // do not use asyncIO for small files
                    await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);

                    logger.LogInformation($"Successfully downloaded latest state '{lastStateBlobEntry}' to '{localDbPath}'");
                }

                await using var con = new SqliteConnection($"Data Source={localDbPath}");
                await con.OpenAsync();

                await using var cmd     = con.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type == 'table' and name == 'BinaryProperties'";
                var             version = (long)cmd.ExecuteScalar();

                if (version == 1)
                {
                    // this is a V2 database
                    throw new NotImplementedException();
                }

            }
        }

        public Repository.StateDbContext GetContext()
        {
            if (!File.Exists(localDbPath))
                throw new InvalidOperationException("The state database file does not exist. Was it already committed?"); //TODO test?

            return new Repository.StateDbContext(localDbPath, HasChanges);
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
            if (!hasChanges)
            {
                logger.LogInformation("No changes made in this version, skipping upload.");
                return;
            }

            // Compact the db
            var vacuumedDbPath = Path.GetTempFileName();

            await using var db = GetContext();
            await db.Database.ExecuteSqlRawAsync($"VACUUM main INTO '{vacuumedDbPath}';"); //https://www.sqlitetutorial.net/sqlite-vacuum/

            var originalLength = new FileInfo(localDbPath).Length;
            var vacuumedlength = new FileInfo(vacuumedDbPath).Length;

            if (originalLength != vacuumedlength)
                logger.LogInformation($"Vacuumed database from {originalLength.GetBytesReadable()} to {vacuumedlength.GetBytesReadable()}");

            // Upload the db
            var b = await container.States.GetBlobAsync($"{versionUtc:s}");
            await using (var ss = File.OpenRead(vacuumedDbPath)) //do not convert to inline using; the File.Delete will fail
            {
                await using var ts = await b.OpenWriteAsync(throwOnExists: false); //the throwOnExists: false is a hack for the unit tests, they run in rapid  succession and the DateTimeNowUtc is the same
                await CryptoService.CompressAndEncryptAsync(ss, ts, passphrase);
            }

            await b.SetAccessTierAsync(AccessTier.Cold);
            await b.SetContentTypeAsync(CryptoService.ContentType);

            // Move the previous states to Archive storage
            await foreach (var be in container.States.GetBlobEntriesAsync()
                               .OrderBy(be => be.Name)
                               .SkipLast(2)
                               .Where(be => be.AccessTier != AccessTier.Archive))
            {
                var b0  = await container.States.GetBlobAsync(be);
                await b0!.SetAccessTierAsync(AccessTier.Archive);
            }

            //Delete the original database
            //await db.Database.EnsureDeletedAsync(); -- we re deleting the temp db when we dispose the Repository, to enable long lived Facades

            var p = $"arius-{versionUtc.ToString("o").Replace(":", "-")}.sqlite";
            File.Move(vacuumedDbPath, p);

            logger.LogInformation($"State upload succesful into '{b}'");
        }

        // --------- FINALIZER ---------
        // See https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose#implement-the-dispose-pattern

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~StateDbContextFactory()
        {
            Dispose(false); // this is weird but according to the best practice
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Delete the temporary db
                using var db = GetContext();
                db.Database.EnsureDeleted();
            }
        }
    }
}