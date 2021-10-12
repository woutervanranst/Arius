using System;
using System.Collections.Generic;
using System.Net;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        internal interface IOptions
        {
            string AccountName { get; }
            string AccountKey { get; }
            string Container { get; }
            string Passphrase { get; }
        }

        public Repository(ILogger<Repository> logger, IOptions options)
        {
            this.logger = logger;
            this.passphrase = options.Passphrase;

            pfeRepo = new(logger, options);
            bmRepo = new(logger, options);


            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
            var bsc = new BlobServiceClient(connectionString);
            container = bsc.GetBlobContainerClient(options.Container);

            var r = container.CreateIfNotExists(PublicAccessType.None);
            if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
                this.logger.LogInformation($"Created container {options.Container}... ");
        }

        private readonly ILogger<Repository> logger;
        private readonly string passphrase;

        private readonly CachedEncryptedPointerFileEntryRepository pfeRepo;
        private readonly CachedBinaryMetadataRepository bmRepo;

        private readonly BlobContainerClient container;
    }
}
