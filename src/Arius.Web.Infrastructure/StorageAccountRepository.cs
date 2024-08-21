using Arius.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arius.Web.Infrastructure;

public class StorageAccountRepository : IStorageAccountRepository
{
    private readonly ApplicationDbContext _context;

    public StorageAccountRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<StorageAccount>> GetAllAsync()
    {
        return await _context.StorageAccounts.ToListAsync();
    }

    public async Task<StorageAccount> GetByIdAsync(int id)
    {
        return await _context.StorageAccounts.FindAsync(id);
    }

    public async Task AddAsync(StorageAccount storageAccount)
    {
        _context.StorageAccounts.Add(storageAccount);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(StorageAccount storageAccount)
    {
        _context.StorageAccounts.Update(storageAccount);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var storageAccount = await _context.StorageAccounts.FindAsync(id);
        if (storageAccount != null)
        {
            _context.StorageAccounts.Remove(storageAccount);
            await _context.SaveChangesAsync();
        }
    }
}