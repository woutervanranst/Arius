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
        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
        var container = new BlobContainerClient(connectionString, options.Container);

        var r0 = container.CreateIfNotExists(PublicAccessType.None);
        if (r0 is not null && r0.GetRawResponse().Status == (int)HttpStatusCode.Created)
            logger.LogInformation($"Created container {options.Container}... ");

        Binaries = new(loggerFactory.CreateLogger<BinaryRepository>(), this, chunker, container);
        Chunks = new(loggerFactory.CreateLogger<ChunkRepository>(), this, container, options.Passphrase);
        PointerFileEntries = new(loggerFactory.CreateLogger<PointerFileEntryRepository>(), this);
        States = new(loggerFactory.CreateLogger<StateRepository>(), this, container, options.Passphrase);
    }
}