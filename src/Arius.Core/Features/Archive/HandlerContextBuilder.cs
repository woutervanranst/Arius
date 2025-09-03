using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Features.Archive;

internal class HandlerContextBuilder
{
    private readonly ArchiveCommand                 request;
    private readonly ILogger<HandlerContextBuilder> logger;
    private readonly ILoggerFactory                 loggerFactory;

    private IArchiveStorage? archiveStorage;
    private StateRepository? stateRepository;
    private IFileSystem?     baseFileSystem;
    private DirectoryInfo?   stateCacheRoot;

    public HandlerContextBuilder(ArchiveCommand request, ILoggerFactory loggerFactory)
    {
        this.request       = request;
        this.loggerFactory = loggerFactory;
        this.logger        = loggerFactory.CreateLogger<HandlerContextBuilder>();
    }

    public HandlerContextBuilder WithArchiveStorage(IArchiveStorage archiveStorage)
    {
        this.archiveStorage = archiveStorage;
        return this;
    }

    public HandlerContextBuilder WithStateRepository(StateRepository stateRepository)
    {
        this.stateRepository = stateRepository;
        return this;
    }

    public HandlerContextBuilder WithBaseFileSystem(IFileSystem fileSystem)
    {
        baseFileSystem = fileSystem;
        return this;
    }

    public HandlerContextBuilder WithStateCacheRoot(DirectoryInfo stateCacheRoot)
    {
        this.stateCacheRoot = stateCacheRoot;
        return this;
    }

    public async Task<HandlerContext> BuildAsync()
    {
        await new ArchiveCommandValidator().ValidateAndThrowAsync(request);

        // Blob Storage
        if (archiveStorage == null)
        {
            var blobStorage = new AzureBlobStorage(request.AccountName, request.AccountKey, request.ContainerName, request.UseRetryPolicy);
            archiveStorage = new EncryptedCompressedStorage(blobStorage, request.Passphrase);
        }

        // Create blob container if needed
        request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 0));
        var created = await archiveStorage.CreateContainerIfNotExistsAsync();
        request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 100, created ? "Created" : "Already existed"));

        return new HandlerContext
        {
            Request         = request,
            ArchiveStorage  = archiveStorage,
            StateRepository = stateRepository ?? await BuildStateRepositoryAsync(archiveStorage),
            Hasher          = new Sha256Hasher(request.Passphrase),
            FileSystem      = GetFileSystem()
        };

        async Task<StateRepository> BuildStateRepositoryAsync(IArchiveStorage archiveStorage)
        {
            // Instantiate StateCache
            var stateCachePath = stateCacheRoot ?? new DirectoryInfo("statecache");
            var stateCache     = new StateCache(stateCachePath);

            // Determine the version name for this run
            var versionName = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss");
            request.ProgressReporter?.Report(new TaskProgressUpdate($"Determining version name '{versionName}'...", 0));

            // Get the latest state from blob storage
            var latestStateName = await archiveStorage.GetStates().LastOrDefaultAsync();

            request.ProgressReporter?.Report(new TaskProgressUpdate($"Getting latest state...", 0, latestStateName is null ? "No previous state found" : $"Latest state: {latestStateName}"));
            FileInfo stateFile;
            if (latestStateName is not null)
            {
                // Download the latest version from blob storage into a `statecache` folder, copy it to the new version and create a new staterepository with the new version
                var latestStateFile = stateCache.GetStateFilePath(latestStateName);
                if (!latestStateFile.Exists)
                {
                    await archiveStorage.DownloadStateAsync(latestStateName, latestStateFile);
                }
                else
                {
                    request.ProgressReporter?.Report(new TaskProgressUpdate($"State file '{latestStateName}' already exists in cache", 100));
                }

                stateFile = stateCache.GetStateFilePath(versionName);
                stateCache.CopyStateFile(latestStateFile, stateFile);
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Copied latest state to new version", 100));
            }
            else
            {
                // If there is none, just create an empty staterepository with the new version
                stateFile = stateCache.GetStateFilePath(versionName);
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Created empty state for new version", 100));
            }

            var contextPool = new StateRepositoryDbContextPool(stateFile, true, loggerFactory.CreateLogger<StateRepositoryDbContextPool>());
            return new StateRepository(contextPool);
        }

        FilePairFileSystem GetFileSystem()
        {
            if (baseFileSystem == null)
            {
                var pfs  = new PhysicalFileSystem();
                var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
                baseFileSystem = new SubFileSystem(pfs, root, true);
            }

            return new FilePairFileSystem(baseFileSystem, true);
        }
    }
}