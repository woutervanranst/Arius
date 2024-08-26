using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IStateDbRepositoryFactory
{
    public Task<IStateDbRepository> CreateAsync(RepositoryOptions repositoryOptions, RepositoryVersion? version = null);
}

public interface IStateDbRepository
{
    RepositoryVersion                  Version { get; }
    IAsyncEnumerable<PointerFileEntry> GetPointerFileEntries();
    IAsyncEnumerable<string>           GetBinaryEntries();
}