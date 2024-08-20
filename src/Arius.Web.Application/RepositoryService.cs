using Arius.Core.Queries.ContainerNames;
using Arius.Web.Core;
using MediatR;

namespace Arius.Web.Application;

public class RepositoryService
{
    private readonly IRepositoryOptionsRepository repositories;
    private readonly IMediator                    mediator;

    public RepositoryService(IRepositoryOptionsRepository repositories, IMediator mediator)
    {
        this.repositories = repositories;
        this.mediator     = mediator;
    }

    public Task<RepositoryOptions>              GetRepositoryByIdAsync(int id) => repositories.GetByIdAsync(id);
    public Task<IEnumerable<RepositoryOptions>> GetAllRepositoriesAsync()         => repositories.GetAllAsync();

    public async Task AddRepositoryAsync(RepositoryOptions config)
    {
        //var q = new ContainerNamesQuery(config.AccountName, config.AccountKey);

        //var r = await mediator.Send(q);

        //var rr = await r.ToListAsync();


        await repositories.AddAsync(config);
    }

    public Task UpdateRepositoryAsync(RepositoryOptions config) => repositories.UpdateAsync(config);
    public Task RemoveRepositoryAsync(int id)                   => repositories.DeleteAsync(id);
}