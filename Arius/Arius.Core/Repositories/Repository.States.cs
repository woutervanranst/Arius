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

internal partial class Repository
{
    public StateRepository States { get; init; }

    internal class StateRepository
    {
        internal const string StateDbsFolderName = "states";

        public StateRepository(ILogger<StateRepository> logger, Repository parent, BlobContainerClient container, string passphrase)
        {
            this.logger = logger;
            this.repo = parent;
            this.container = container;
            this.passphrase = passphrase;

            // download latest state asynchronously
            dbPathTask = Task.Run(GetLastestStateDbAsync);
        }

        private readonly ILogger<StateRepository> logger;
        private readonly Repository repo;
        private readonly BlobContainerClient container;
        private readonly string passphrase;
        private readonly Task<string> dbPathTask;
        private bool hasChanges;


        private async Task<string> GetLastestStateDbAsync()
        {
            var lastStateBlobName = await container.GetBlobsAsync(prefix: $"{StateDbsFolderName}/")
                .Select(bi => bi.Name)
                .OrderBy(n => n)
                .LastOrDefaultAsync();

            var localDbPath = Path.GetTempFileName(); //TODO write this to the /log directory so it is outside of the container in case of a crash
            if (lastStateBlobName is null)
            {
                await using var db = new AriusDbContext(localDbPath, HasChanges);
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

            return localDbPath;
        }

        internal void SetMockedDbContext(AriusDbContext mockedContext) => this.mockedContext = mockedContext;
        private AriusDbContext mockedContext;


        internal async Task<AriusDbContext> GetCurrentStateDbContextAsync()
        {
            if (mockedContext is not null)
                return mockedContext;

            var dbPath = await dbPathTask;
            if (!File.Exists(dbPath))
                throw new InvalidOperationException("The state database file does not exist. Was it already committed?"); //TODO test?

            return new(await dbPathTask, HasChanges);
        }

        private void HasChanges(int numChanges)
        {
            if (numChanges > 0)
                hasChanges = true;

            logger.LogDebug($"{numChanges} state entries written to the database");
        }

        internal async Task CommitToBlobStorageAsync(DateTime versionUtc)
        {
            if (!hasChanges)
            {
                logger.LogInformation("No changes made in this version, skipping upload.");
                return;
            }

            var vacuumedDbPath = Path.GetTempFileName();

            await using var db = await GetCurrentStateDbContextAsync();
            await db.Database.ExecuteSqlRawAsync($"VACUUM main INTO '{vacuumedDbPath}';"); //https://www.sqlitetutorial.net/sqlite-vacuum/

            var originalLength = new FileInfo(await dbPathTask).Length;
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
            await db.Database.EnsureDeletedAsync();
            var p = $"arius-{versionUtc.ToString("o").Replace(":", "-")}.sqlite";
            if (new DirectoryInfo("logs").Exists) //TODO remove the "logs" magic string
                // Not running in container
                p = Path.Combine("logs", p);
            else if (new DirectoryInfo("/logs").Exists) //TODO remove the "logs" magic string
                // Running in container
                p = Path.Combine("/logs", p);
            if (new FileInfo(p).Directory.Exists)
            {
                logger.LogInformation($"Moved vacuumed db to {p}");
                File.Move(vacuumedDbPath, p);
            }
            else
                logger.LogInformation($"Vacuumed db was not moved because the destination directory of {p} does not exist");

            logger.LogInformation($"State upload succesful into '{blobName}'");
        }
    }
}