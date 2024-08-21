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
    private readonly IStorageAccountRepository _storageAccountRepository;
    private readonly IRepositoryRepository     _repositoryRepository;

    public RepositoryService(
        IStorageAccountRepository storageAccountRepository,
        IRepositoryRepository repositoryRepository)
    {
        _storageAccountRepository = storageAccountRepository;
        _repositoryRepository     = repositoryRepository;
    }

    // StorageAccount Methods

    public async Task<List<StorageAccount>> GetStorageAccountsAsync()
    {
        return await _storageAccountRepository.GetAllAsync();
    }

    public async Task<StorageAccount> GetStorageAccountByIdAsync(int id)
    {
        return await _storageAccountRepository.GetByIdAsync(id);
    }

    public async Task AddStorageAccountAsync(StorageAccount storageAccount)
    {
        await _storageAccountRepository.AddAsync(storageAccount);
    }

    public async Task UpdateStorageAccountAsync(StorageAccount storageAccount)
    {
        await _storageAccountRepository.UpdateAsync(storageAccount);
    }

    public async Task DeleteStorageAccountAsync(int id)
    {
        await _storageAccountRepository.DeleteAsync(id);
    }

    // Repository Methods

    public async Task<List<Repository>> GetRepositoriesAsync()
    {
        return await _repositoryRepository.GetAllAsync();
    }

    public async Task<Repository> GetRepositoryByIdAsync(int id)
    {
        return await _repositoryRepository.GetByIdAsync(id);
    }

    public async Task AddRepositoryAsync(Repository repository)
    {
        await _repositoryRepository.AddAsync(repository);
    }

    public async Task UpdateRepositoryAsync(Repository repository)
    {
        await _repositoryRepository.UpdateAsync(repository);
    }

    public async Task DeleteRepositoryAsync(int id)
    {
        await _repositoryRepository.DeleteAsync(id);
    }
}