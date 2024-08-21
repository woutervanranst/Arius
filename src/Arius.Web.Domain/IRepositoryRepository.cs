namespace Arius.Web.Domain;

public interface IRepositoryRepository
{
    Task<List<Repository>> GetAllAsync();
    Task<Repository>       GetByIdAsync(int id);
    Task                   AddAsync(Repository repository);
    Task                   UpdateAsync(Repository repository);
    Task                   DeleteAsync(int id);
}