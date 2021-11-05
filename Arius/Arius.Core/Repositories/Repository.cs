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
        this.logger = loggerFactory.CreateLogger<Repository>();
        this.passphrase = options.Passphrase;

        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
        container = new BlobContainerClient(connectionString, options.Container);

        var r0 = container.CreateIfNotExists(PublicAccessType.None);
        if (r0 is not null && r0.GetRawResponse().Status == (int)HttpStatusCode.Created)
            this.logger.LogInformation($"Created container {options.Container}... ");

        // download db
        Task.Run(async () =>
        {
            AriusDbContext.DbPathTask.SetResult(@"c:\ha.sqlite"/*Path.GetTempFileName()*/);
        });

        BinaryMetadata = new BinaryMetadataRepository(loggerFactory.CreateLogger<BinaryMetadataRepository>());
        BinaryManifests = new BinaryManifestRepository(loggerFactory.CreateLogger<BinaryManifestRepository>(), this, container);





        pfeRepo = new(loggerFactory.CreateLogger<AppendOnlyRepository<PointerFileEntry>>(), options, container, POINTER_FILE_ENTRIES_FOLDER_NAME);
        versionsTask = Task.Run(async () =>
        {
            var entries = await pfeRepo.GetAllItemsAsync();
            return new SortedSet<DateTime>(entries.Select(pfe => pfe.VersionUtc).Distinct());
        });


        //var tsc = new TableServiceClient(connectionString);
        //bmTable = tsc.GetTableClient($"{options.Container}{BINARY_METADATA_TABLE_NAME_SUFFIX}");

        //var r1 = bmTable.CreateIfNotExists();
        //if (r1 is not null)
        //    logger.LogInformation($"Created {bmTable.Name} table");
    }

    private readonly ILogger<Repository> logger;
    private readonly string passphrase;

    private readonly BlobContainerClient container;




    private readonly AppendOnlyRepository<PointerFileEntry> pfeRepo;
    private const string POINTER_FILE_ENTRIES_FOLDER_NAME = "pointerfileentries";
    private readonly Task<SortedSet<DateTime>> versionsTask;

    //private readonly TableClient bmTable;
    //private const string BINARY_METADATA_TABLE_NAME_SUFFIX = "binarymetadata";
}