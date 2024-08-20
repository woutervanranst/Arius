namespace Arius.Web.Core;

public interface IRepositoryOptionsRepository
{
    Task<RepositoryOptions>              GetByIdAsync(int id);
    Task<IEnumerable<RepositoryOptions>> GetAllAsync();
    Task                                 AddAsync(RepositoryOptions config);
    Task                                 UpdateAsync(RepositoryOptions config);
    Task                                 DeleteAsync(int id);
}