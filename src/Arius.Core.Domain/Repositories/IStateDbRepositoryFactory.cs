using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IStateDbRepositoryFactory
{
    public Task<IStateRepository> CreateAsync(RepositoryOptions repositoryOptions, RepositoryVersion? version = null);
    Task                            SaveChangesAsync(IStateRepository repository);
}