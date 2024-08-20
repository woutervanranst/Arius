using Arius.Core.Facade;
using Arius.Core.Repositories.BlobRepository;
using Azure.Core;
using Azure.Storage.Blobs;

namespace Arius.Core.Repositories;

internal partial class RepositoryBuilder
{
    private readonly ILogger<Repository> logger;

    public RepositoryBuilder(ILogger<Repository> logger)
    {
        this.logger = logger;
    }

    private RepositoryOptions options;
    private BlobContainer     container;

    public RepositoryBuilder WithOptions(RepositoryOptions options)
    {
        this.options = options;

        // Get the Blob Container Client
        container = new BlobContainer(options.GetBlobContainerClient(GetExponentialBackoffOptions()));

        return this;
    }


    private IStateDbContextFactory dbContextFactory;
    public RepositoryBuilder WithLatestStateDatabase()
    {
        dbContextFactory = new StateDbContextFactory(logger, container, options.Passphrase);
        
        return this;
    }

    //public RepositoryBuilder WithMockedDatabase(Repository.AriusDbContext mockedContext)
    //{
    //    dbContextFactory = new Repository.AriusDbContextMockedFactory(mockedContext);

    //    return this;
    //}

    public async Task<Repository> BuildAsync()
    {
        if (options == default)
            throw new ArgumentException("Options not set");

        // Ensure the Blob Container exists
        if (await container.CreateIfNotExistsAsync())
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
        return new BlobClientOptions
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