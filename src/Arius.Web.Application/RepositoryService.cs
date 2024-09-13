using Arius.Core.Domain.Storage;
using Arius.Core.New.Queries.ValidateStorageAccountCredentials;
using Arius.Web.Domain;
using MediatR;

namespace Arius.Web.Application;

//public class RepositoryService
//{
//    private readonly IMediator                    mediator;

//    public RepositoryService(IRepositoryOptionsRepository repositories, IMediator mediator)
//    {
//        this.mediator     = mediator;
//    }

//    public async Task AddRepositoryAsync(CloudRepositoryOptions config)
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
    private readonly IMediator                 mediator;

    public RepositoryService(
        IStorageAccountRepository storageAccountRepository,
        IRepositoryRepository repositoryRepository,
        IMediator mediator)
    {
        this.storageAccountRepository = storageAccountRepository;
        this.repositoryRepository     = repositoryRepository;
        this.mediator                 = mediator;
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

    public async Task<(bool Success, string? ErrorMessage)> UpdateStorageAccountAsync(StorageAccount storageAccount)
    {
        var q = new ValidateStorageAccountCredentialsQuery
        {
            StorageAccount = new StorageAccountOptions
            {
                AccountName = storageAccount.AccountName,
                AccountKey  = storageAccount.AccountKey
            }
        };

        if (!await mediator.Send(q))
            return (false, "Invalid credentials");

        await storageAccountRepository.UpdateAsync(storageAccount);
        return (true, null);
    }

    public async Task DeleteStorageAccountAsync(int id)
    {
        await storageAccountRepository.DeleteAsync(id);
    }


    // CloudRepository Methods

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