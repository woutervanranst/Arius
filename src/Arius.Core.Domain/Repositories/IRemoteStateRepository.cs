using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IRemoteStateRepository
{
    IAsyncEnumerable<RepositoryVersion> GetStateDatabaseVersions();

}