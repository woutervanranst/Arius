using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Microsoft.Extensions.Options;

namespace Arius.Core.Infrastructure.Repositories;

public class SqliteStateDbRepositoryFactory : IStateDbRepositoryFactory
{
    private readonly AriusConfiguration config;

    public SqliteStateDbRepositoryFactory(IOptions<AriusConfiguration> config)
    {
        this.config = config.Value;
    }
    public Task<IStateDbRepository> CreateAsync(RepositoryOptions repository)
    {
        throw new NotImplementedException();
    }
}

public class SqliteStateDbRepository : IStateDbRepository
{

}