﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
            this.parent = parent;
            this.container = container;
            this.passphrase = passphrase;

            // download latest state
            dbPathTask = Task.Run(async () => await GetLastestStateDb());
        }

        private readonly ILogger<StateRepository> logger;
        private readonly Repository parent;
        private readonly BlobContainerClient container;
        private readonly string passphrase;
        private readonly Task<string> dbPathTask;
        private bool hasChanges;


        private async Task<string> GetLastestStateDb()
        {
            var lastStateBlobName = await container.GetBlobsAsync(prefix: $"{StateDbsFolderName}/")
                .Select(bi => bi.Name)
                .OrderBy(n => n)
                .LastOrDefaultAsync();

            var localDbPath = Path.GetTempFileName();
            if (lastStateBlobName is null)
            {
                await using var db = new AriusDbContext(localDbPath, HasChanges);
                await db.Database.EnsureCreatedAsync();

                logger.LogInformation("Created new state database");
            }
            else
            {
                await using var ss = await container.GetBlobClient(lastStateBlobName).OpenReadAsync();
                await using var ts = File.OpenWrite(localDbPath);
                await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);

                logger.LogInformation($"Successfully downloaded state version {lastStateBlobName}");
            }

            return localDbPath;
        }

        internal async Task<AriusDbContext> GetCurrentStateDbContext()
        {
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

        internal async Task SaveToBlobStorage(DateTime versionUtc)
        {
            if (!hasChanges)
            {
                logger.LogInformation("No changes made in this version, skipping upload.");
                return;
            }

            var vacuumedDbPath = Path.GetTempFileName();

            await using var db = await GetCurrentStateDbContext();
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

            //Delete the original database and the compressed file
            await db.Database.EnsureDeletedAsync();
            File.Delete(vacuumedDbPath);

            logger.LogInformation($"State upload succesful into '{blobName}'");
        }
    }
}