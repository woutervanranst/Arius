using System;
using System.Net;
using System.Threading.Tasks;
using Arius.Core.Facade;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

internal class RepositoryBuilder
{
    private readonly ILogger<Repository> logger;

    public RepositoryBuilder(ILogger<Repository> logger)
    {
        this.logger = logger;
    }

    private IRepositoryOptions  options   = default;
    private BlobContainerClient container = default;

    public RepositoryBuilder WithOptions(IRepositoryOptions options)
    {
        this.options = options;

        // Get the Blob Container Client
        container = options.GetBlobContainerClient(GetExponentialBackoffOptions());

        return this;
    }


    private Repository.IStateDbContextFactory dbContextFactory;
    public RepositoryBuilder WithLatestStateDatabase()
    {
        dbContextFactory = new Repository.StateDbContextFactory(logger, container, options.Passphrase);
        
        return this;
    }

    //public RepositoryBuilder WithMockedDatabase(Repository.AriusDbContext mockedContext)
    //{
    //    dbContextFactory = new Repository.AriusDbContextMockedFactory(mockedContext);

    //    return this;
    //}

    public async Task<Repository> BuildAsync()
    {
        if (options == default(IRepositoryOptions))
            throw new ArgumentException("Options not set");
        

        // Ensure the Blob Container exists
        var r = await container.CreateIfNotExistsAsync(PublicAccessType.None);
        if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
            logger.LogInformation($"Created container {options.ContainerName}... ");

        // Initialize the DbContextFactory (ie. download the state from blob)
        await dbContextFactory.LoadAsync();

        return new Repository(logger, options, dbContextFactory, container);
    }

    private static BlobClientOptions GetExponentialBackoffOptions()
    {
        /* 
         * RequestFailedException: The condition specified using HTTP conditional header(s) is not met.
         *      -- this is a throttling error most likely, hence specifiying exponential backoff
         *      as per https://docs.microsoft.com/en-us/azure/architecture/best-practices/retry-service-specific#blobs-queues-and-files
         */
        return new BlobClientOptions()
        {
            Retry =
            {
                Delay      = TimeSpan.FromSeconds(2),  //The delay between retry attempts for a fixed approach or the delay on which to base calculations for a backoff-based approach
                MaxRetries = 10,                       //The maximum number of retry attempts before giving up
                Mode       = RetryMode.Exponential,    //The approach to use for calculating retry delays
                MaxDelay   = TimeSpan.FromSeconds(120) //The maximum permissible delay between retry attempts
            }
        };
    }
}