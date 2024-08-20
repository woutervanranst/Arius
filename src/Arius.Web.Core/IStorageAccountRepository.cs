namespace Arius.Web.Core;

public interface IStorageAccountRepository
{
    Task<List<StorageAccount>> GetAllAsync();
    Task<StorageAccount>       GetByIdAsync(int id);
    Task                       AddAsync(StorageAccount storageAccount);
    Task                       UpdateAsync(StorageAccount storageAccount);
    Task                       DeleteAsync(int id);
}