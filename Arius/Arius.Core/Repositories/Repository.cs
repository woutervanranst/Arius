using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Arius.Core.Models;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    internal interface IOptions
    {
        string AccountName { get; }
        string AccountKey { get; }
        string Container { get; }
        string Passphrase { get; }
    }

    public Repository(ILoggerFactory loggerFactory, IOptions options, Chunker chunker)
    {
        var logger = loggerFactory.CreateLogger<Repository>();
        var passphrase = options.Passphrase;

        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
        container = new BlobContainerClient(connectionString, options.Container);

        var r0 = container.CreateIfNotExists(PublicAccessType.None);
        if (r0 is not null && r0.GetRawResponse().Status == (int)HttpStatusCode.Created)
            logger.LogInformation($"Created container {options.Container}... ");

        // download db
        Task.Run(async () =>
        {
            var path = await GetLastestStateDb(container, passphrase);
            //var path = @"c:\ha.sqlite"; /*Path.GetTempFileName()*/;
            //await AriusDbContext.EnsureCreated(path);
            AriusDbContext.HasChanges = false;
            AriusDbContext.DbPathTask.SetResult(path);
        });

        Binaries = new(loggerFactory.CreateLogger<BinaryRepository>(), this, chunker, container);
        Chunks = new(loggerFactory.CreateLogger<ChunkRepository>(), this, container, passphrase);
        PointerFileEntries = new(loggerFactory.CreateLogger<PointerFileEntryRepository>(), this);
    }

    private readonly BlobContainerClient container;

    private static async Task<string> GetLastestStateDb(BlobContainerClient container, string passphrase)
    {
        var lastStateBlobName = container.GetBlobs(prefix: $"{StateDbsFolderName}/")
            .Select(bi => bi.Name)
            .OrderBy(n => n)
            .LastOrDefault();

        var localDbPath = Path.GetTempFileName();
        if (lastStateBlobName is null)
        {
            await AriusDbContext.EnsureCreated(localDbPath);
        }
        else
        {
            await using var ss = await container.GetBlobClient(lastStateBlobName).OpenReadAsync();
            await using var ts = File.OpenWrite(localDbPath);
            await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
        }

        return localDbPath;
    }

    internal async Task SaveStateDb(DateTime versionUtc, string passphrase)
    {
        if (!AriusDbContext.HasChanges)
            return;

        var dbpath = Path.GetTempFileName();

        await using (var db = await AriusDbContext.GetAriusDbContext())
        {
            await db.Database.ExecuteSqlRawAsync($"VACUUM main INTO '{dbpath}';");
            //.ExecuteSqlCommand(TransactionalBehavior.DoNotEnsureTransaction, "VACUUM;");
        }

        var orig = new FileInfo((await AriusDbContext.DbPathTask.Task)).Length;
        var vacc = new FileInfo(dbpath).Length;

        var n = $"{StateDbsFolderName}/{versionUtc:s}";

        var bbc = container.GetBlockBlobClient(n);
        await using var ts = await bbc.OpenWriteAsync(overwrite: true);
        await using var ss = File.OpenRead(dbpath);
        await CryptoService.CompressAndEncryptAsync(ss, ts, passphrase);
        await bbc.SetAccessTierAsync(AccessTier.Cool);
    }

    internal const string StateDbsFolderName = "states";
}