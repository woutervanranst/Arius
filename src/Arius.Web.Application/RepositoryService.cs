using Arius.Web.Domain;

namespace Arius.Web.Application;

//public class RepositoryService
//{
//    private readonly IMediator                    mediator;

//    public RepositoryService(IRepositoryOptionsRepository repositories, IMediator mediator)
//    {
//        this.mediator     = mediator;
//    }

//    public async Task AddRepositoryAsync(RepositoryOptions config)
//    {
//        //var q = new ContainerNamesQuery(config.AccountName, config.AccountKey);

//        //var r = await mediator.Send(q);

//        //var rr = await r.ToListAsync();
//    }
//}

public class RepositoryService
{
    private readonly IStorageAccountRepository storageAccountRepository;
    private readonly IRepositoryRepository     repositoryRepository;

    public RepositoryService(
        IStorageAccountRepository storageAccountRepository,
        IRepositoryRepository repositoryRepository)
    {
        this.storageAccountRepository = storageAccountRepository;
        this.repositoryRepository     = repositoryRepository;
    }

    // StorageAccount Methods

    public async Task<List<StorageAccount>> GetStorageAccountsAsync()
    {
        return await storageAccountRepository.GetAllAsync();
    }

    public async Task<StorageAccount> GetStorageAccountByIdAsync(int id)
    {
        return await storageAccountRepository.GetByIdAsync(id);
    }

    public async Task AddStorageAccountAsync(StorageAccount storageAccount)
    {
        await storageAccountRepository.AddAsync(storageAccount);
    }

    public async Task UpdateStorageAccountAsync(StorageAccount storageAccount)
    {
        await storageAccountRepository.UpdateAsync(storageAccount);
    }

    public async Task DeleteStorageAccountAsync(int id)
    {
        await storageAccountRepository.DeleteAsync(id);
    }

    // Repository Methods

    public async Task<List<Repository>> GetRepositoriesAsync()
    {
        return await repositoryRepository.GetAllAsync();
    }

    public async Task<Repository> GetRepositoryByIdAsync(int id)
    {
        return await repositoryRepository.GetByIdAsync(id);
    }

    public async Task AddRepositoryAsync(Repository repository)
    {
        await repositoryRepository.AddAsync(repository);
    }

    public async Task UpdateRepositoryAsync(Repository repository)
    {
        await repositoryRepository.UpdateAsync(repository);
    }

    public async Task DeleteRepositoryAsync(int id)
    {
        await repositoryRepository.DeleteAsync(id);
    }
}