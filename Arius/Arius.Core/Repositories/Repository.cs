using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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

    public Repository(ILoggerFactory loggerFactory, IOptions options)
    {
        var logger = loggerFactory.CreateLogger<Repository>();
        var passphrase = options.Passphrase;

        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
        var container = new BlobContainerClient(connectionString, options.Container);

        var r0 = container.CreateIfNotExists(PublicAccessType.None);
        if (r0 is not null && r0.GetRawResponse().Status == (int)HttpStatusCode.Created)
            logger.LogInformation($"Created container {options.Container}... ");

        // download db
        Task.Run(async () =>
        {
            var path = @"c:\ha.sqlite"; /*Path.GetTempFileName()*/;
            await AriusDbContext.EnsureCreated(path);
            AriusDbContext.DbPathTask.SetResult(path);
        });

        BinaryMetadata = new(loggerFactory.CreateLogger<BinaryMetadataRepository>());
        BinaryManifests = new(loggerFactory.CreateLogger<BinaryManifestRepository>(), this, container);
        Chunks = new(loggerFactory.CreateLogger<ChunkRepository>(), this, container, passphrase);
        PointerFileEntries = new(loggerFactory.CreateLogger<PointerFileEntryRepository>(), this);
    }
}