using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IRemoteStateRepository
{
    IAsyncEnumerable<RepositoryVersion> GetStateDatabaseVersions();

    /// <summary>
    /// Get an existing repository version. 
    /// If `version` is null, it will get the latest version. If there is no version, it will return null.
    /// If `version` is specified but does not exist, it will throw an exception.
    /// </summary>
    public Task<ILocalStateRepository?> GetLocalStateRepositoryAsync(DirectoryInfo localStateDatabaseCacheDirectory, RepositoryVersion? version = null);

    /// <summary>
    /// Create a new repository version based on an existing one.
    /// If `basedOn` is null, it will be based on the latest version. If there is no version, it will return a new, empty, repository.
    /// If `basedOn` is specified, but does not exist, it will throw an exception.
    /// </summary>
    public Task<ILocalStateRepository> CreateNewLocalStateRepositoryAsync(DirectoryInfo localStateDatabaseCacheDirectory, RepositoryVersion version, RepositoryVersion? basedOn = null);
    
    Task<bool>                         SaveChangesAsync(ILocalStateRepository localStateRepository);
}