using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IRemoteStateRepository
{
    public Task<ILocalStateRepository> CreateAsync(RemoteRepositoryOptions remoteRepositoryOptions, RepositoryVersion? version = null);
    Task                               SaveChangesAsync(ILocalStateRepository localStateRepository, IRemoteRepository remoteRepository);
}