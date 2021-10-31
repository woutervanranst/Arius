using System;
using System.Collections.Generic;
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
        this.logger = loggerFactory.CreateLogger<Repository>();
        this.passphrase = options.Passphrase;

        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
        container = new BlobContainerClient(connectionString, options.Container);

        var r = container.CreateIfNotExists(PublicAccessType.None);
        if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
            this.logger.LogInformation($"Created container {options.Container}... ");

        
        pfeRepo = new(loggerFactory.CreateLogger<AppendOnlyRepository<PointerFileEntry>>(), options, container, PointerFileEntriesFolderName);
        versionsTask = Task.Run(async () =>
        {
            var entries = await pfeRepo.GetEntriesAsync();
            return new SortedSet<DateTime>(entries.Select(pfe => pfe.VersionUtc).Distinct());
        });


        bmRepo = new(loggerFactory.CreateLogger<AppendOnlyRepository<BinaryMetadata>>(), options, container, BinaryMetadataFolderName);
    }

    private readonly ILogger<Repository> logger;
    private readonly string passphrase;

    private readonly BlobContainerClient container;


    private readonly AppendOnlyRepository<PointerFileEntry> pfeRepo;
    private const string PointerFileEntriesFolderName = "pointerfileentries";
    private readonly Task<SortedSet<DateTime>> versionsTask;

    private readonly AppendOnlyRepository<BinaryMetadata> bmRepo;
    private const string BinaryMetadataFolderName = "binarymetadata";
}