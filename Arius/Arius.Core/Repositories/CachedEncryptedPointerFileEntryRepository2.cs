using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        internal class CachedEncryptedPointerFileEntryRepository2
        {
            public CachedEncryptedPointerFileEntryRepository2(ILogger<CachedEncryptedPointerFileEntryRepository2> logger, IOptions options, BlobContainerClient container)
            {
                this.logger = logger;
                this.passphrase = options.Passphrase;

                //var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
                //container = new BlobContainerClient(connectionString, options.Container);
                this.container = container;

                versionsTask = Task.Run(async () =>
                {
                    //var x = container
                    //    .GetBlobs(prefix: $"{PointerFileEntriesFolderName}/")
                    //    .AsParallel()
                    //    .Select(async (bi) =>
                    //    {
                    //        var bc = container.GetBlobClient(bi.Name);

                    //        using var encryptedVersionBlobStream = await bc.OpenReadAsync();
                    //        using var versionMemoryStream = new MemoryStream();

                    //        await CryptoService.DecryptAndDecompressAsync(encryptedVersionBlobStream, versionMemoryStream, passphrase);
                    //        var version = await JsonSerializer.DeserializeAsync<IEnumerable<PointerFileEntry>>(versionMemoryStream);

                    //        return version;

                    //        //versions.AddRange(version.PointerFileEntries);

                    //        //return Task.FromResult(5);

                    //        //var version = DateTime.Parse(bi.Name, System.Globalization.DateTimeStyles.RoundtripKind);
                    //    });

                    //var z = (await Task.WhenAll(x)).SelectMany(x => x).ToList();

                    var versions = new ConcurrentBag<PointerFileEntry>();

                    await Parallel.ForEachAsync(container.GetBlobsAsync(prefix: $"{PointerFileEntriesFolderName}/"), async (bi, ct) =>
                    {
                        var bc = container.GetBlobClient(bi.Name);

                        using var encryptedVersionBlobStream = await bc.OpenReadAsync(cancellationToken: ct);
                        using var versionMemoryStream = new MemoryStream();
                        
                        await CryptoService.DecryptAndDecompressAsync(encryptedVersionBlobStream, versionMemoryStream, passphrase);
                        var pfes = await JsonSerializer.DeserializeAsync<IEnumerable<PointerFileEntry>>(versionMemoryStream, cancellationToken: ct);

                        versions.AddFromEnumerable(pfes);

                        //var version = DateTime.Parse(bi.Name, System.Globalization.DateTimeStyles.RoundtripKind);
                    });

                    return versions;
                });
            }

            private readonly ILogger logger;
            private readonly string passphrase;
            private readonly BlobContainerClient container;
            private const string PointerFileEntriesFolderName = "pointerfileentries";
            private readonly Task<ConcurrentBag<PointerFileEntry>> versionsTask;

            


            public async Task StartNewVersion(DateTime version)
            {
                if (this.version is not null)
                    throw new InvalidOperationException("A version is already open");

                versionFile = Path.GetTempFileName();
            }

            private DateTime? version;
            private string versionFile;

            public async Task<bool> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
            {
                if (this.version is null)
                    throw new InvalidOperationException("No version is open");

                var versions = await versionsTask;

                //versions..SelectMany(x => x).

                var json = JsonSerializer.Serialize(pfe);
                await File.AppendAllLinesAsync(versionFile, json.SingleToArray()); // alternates: https://stackoverflow.com/a/19691606/1582323

                return false;
            }

            public async Task CommitCurrentVersion()
            {
                //if (this.version is null)
                //    throw new InvalidOperationException()
                //var blobName = version.
                //var bc = container.GetBlobClient(bi.Name);

            }

            //private readonly struct kakak
            //{
            //    public DateTime VersionUtc { get; init; }
            //    public List<PointerFileEntry> PointerFileEntries  { get; init; }
            //}
        }
    }
}
