using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Arius.Core.Models;
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

        public Repository(IOptions options, ILogger<Repository> logger, IBlobCopier blobCopier)
        {
            this.logger = logger;
            _blobCopier = blobCopier;

            InitManifestRepository();
            InitChunkRepository();
            InitPointerFileEntryRepository(options, logger, out pfeRepo);

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";

            var bsc = new BlobServiceClient(connectionString);
            container = bsc.GetBlobContainerClient(options.Container);

            var r = container.CreateIfNotExists(PublicAccessType.None);

            if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
                this.logger.LogInformation($"Created container {options.Container}... ");
        }

        private readonly ILogger<Repository> logger;
        private readonly BlobContainerClient container;
    }
}
