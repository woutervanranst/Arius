using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IStateDbRepositoryFactory
{
    public Task<IStateDbRepository> CreateAsync(RepositoryOptions repositoryOptions, RepositoryVersion? version = null);
    Task                            SaveChangesAsync(IStateDbRepository repository);
}