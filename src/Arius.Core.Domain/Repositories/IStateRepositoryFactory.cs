using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IStateRepositoryFactory
{
    public Task<IStateRepository> CreateAsync(CloudRepositoryOptions cloudRepositoryOptions, RepositoryVersion? version = null);
    Task                          SaveChangesAsync(CloudRepositoryOptions cloudRepositoryOptions, IStateRepository stateRepository);
}