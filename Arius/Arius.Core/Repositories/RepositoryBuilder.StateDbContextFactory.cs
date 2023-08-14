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

    private class StateDbContextFactory : IStateDbContextFactory
    {
        private const string StateDbsFolderName = "states";
        
        private readonly ILogger             logger;
        private readonly BlobContainerClient container;
        private readonly string              passphrase;

        private readonly string              localDbPath;

        public StateDbContextFactory(ILogger logger, BlobContainerClient container, string passphrase)
        {
            this.logger     = logger;
            this.container  = container;
            this.passphrase = passphrase;

            localDbPath = Path.GetTempFileName(); //TODO write this to the /log directory so it is outside of the container in case of a crash
        }

        public async Task LoadAsync()
        {
            var lastStateBlobName = await container.GetBlobsAsync(prefix: $"{StateDbsFolderName}/")
                .Select(bi => bi.Name)
                .OrderBy(n => n)
                .LastOrDefaultAsync();

            if (lastStateBlobName is null)
            {
                // Create new DB
                await using var db = new Repository.StateDbContext(localDbPath);
                await db.Database.EnsureCreatedAsync();

                logger.LogInformation($"Created new state database to '{localDbPath}'");
            }
            else
            {
                // Load existing DB
                await using var ss = await container.GetBlobClient(lastStateBlobName).OpenReadAsync();
                await using var ts = new FileStream(localDbPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 4096); //File.OpenWrite(localDbPath); // do not use asyncIO for small files
                await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);

                logger.LogInformation($"Successfully downloaded latest state '{lastStateBlobName}' to '{localDbPath}'");
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

            var vacuumedDbPath = Path.GetTempFileName();

            await using var db = GetContext();
            await db.Database.ExecuteSqlRawAsync($"VACUUM main INTO '{vacuumedDbPath}';"); //https://www.sqlitetutorial.net/sqlite-vacuum/

            var originalLength = new FileInfo(localDbPath).Length;
            var vacuumedlength = new FileInfo(vacuumedDbPath).Length;

            if (originalLength != vacuumedlength)
                logger.LogInformation($"Vacuumed database from {originalLength.GetBytesReadable()} to {vacuumedlength.GetBytesReadable()}");

            var blobName = $"{StateDbsFolderName}/{versionUtc:s}";
            var bbc = container.GetBlockBlobClient(blobName);
            await using (var ss = File.OpenRead(vacuumedDbPath)) //do not convert to inline using; the File.Delete will fail
            {
                await using var ts = await bbc.OpenWriteAsync(overwrite: true);
                await CryptoService.CompressAndEncryptAsync(ss, ts, passphrase);
            }

            await bbc.SetAccessTierAsync(AccessTier.Cool);
            await bbc.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = CryptoService.ContentType });

            // Move the previous states to Archive storage
            await foreach (var bi in container.GetBlobsAsync(prefix: $"{StateDbsFolderName}/")
                               .OrderBy(bi => bi.Name)
                               .SkipLast(2)
                               .Where(bi => bi.Properties.AccessTier != AccessTier.Archive))
            {
                var bc = container.GetBlobClient(bi.Name);
                await bc.SetAccessTierAsync(AccessTier.Archive);
            }

            //Delete the original database
            //await db.Database.EnsureDeletedAsync(); -- we re deleting the temp db when we dispose the Repository, to enable long lived Facades

            var p = $"arius-{versionUtc.ToString("o").Replace(":", "-")}.sqlite";
            File.Move(vacuumedDbPath, p);

            logger.LogInformation($"State upload succesful into '{blobName}'");
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

        protected virtual void Dispose(bool disposing)
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