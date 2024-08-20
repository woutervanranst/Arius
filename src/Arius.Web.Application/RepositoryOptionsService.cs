using Arius.Core.Facade;
using Arius.Web.Core;
using MediatR;

namespace Arius.Web.Application;

public class RepositoryOptionsService
{
    private readonly IRepositoryOptionsRepository repositoryOptionsRepository;
    private readonly IMediator                    mediator;

    public RepositoryOptionsService(IRepositoryOptionsRepository repositoryOptionsRepository, IMediator mediator)
    {
        this.repositoryOptionsRepository = repositoryOptionsRepository;
        this.mediator                    = mediator;
    }

    public Task<RepositoryOptions>              GetRepositoryOptions(int id)                           => repositoryOptionsRepository.GetByIdAsync(id);
    public Task<IEnumerable<RepositoryOptions>> GetRepositoryOptionsAsync()                            => repositoryOptionsRepository.GetAllAsync();

    public async Task AddRepositoryOptionsAsync(RepositoryOptions config)
    {
        var q = new Arius.Core.Queries.ContainerNames2.ContainerNamesQuery2
        {
            AccountName = config.AccountName,
            AccountKey  = config.AccountKey,
            MaxRetries  = 1
        };

        var r = await mediator.Send(q);


        await repositoryOptionsRepository.AddAsync(config);
    }
    public Task                                 UpdateRepositoryOptionsAsync(RepositoryOptions config) => repositoryOptionsRepository.UpdateAsync(config);
    public Task                                 RemoveRepositoryOptionsAsync(int id)                     => repositoryOptionsRepository.DeleteAsync(id);
}