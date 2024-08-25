using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IStateDbRepositoryFactory
{
    public Task<IStateDbRepository> CreateAsync(RepositoryOptions repository);
}

public interface IStateDbRepository
{

}