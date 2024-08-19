using Arius.Web.Core;

namespace Arius.Web.Application;

public class RepositoryOptionsService
{
    private readonly IRepositoryOptionsRepository repositoryOptionsRepository;

    public RepositoryOptionsService(IRepositoryOptionsRepository repositoryOptionsRepository)
    {
        this.repositoryOptionsRepository = repositoryOptionsRepository;
    }

    public Task<RepositoryOptions>              GetRepositoryOptions(int id)                           => repositoryOptionsRepository.GetByIdAsync(id);
    public Task<IEnumerable<RepositoryOptions>> GetRepositoryOptionsAsync()                            => repositoryOptionsRepository.GetAllAsync();
    public Task                                 AddRepositoryOptionsAsync(RepositoryOptions config)    => repositoryOptionsRepository.AddAsync(config);
    public Task                                 UpdateRepositoryOptionsAsync(RepositoryOptions config) => repositoryOptionsRepository.UpdateAsync(config);
    public Task                                 RemoveRepositoryOptionsAsync(int id)                     => repositoryOptionsRepository.DeleteAsync(id);
}