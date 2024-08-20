using Arius.Web.Core;
using Microsoft.EntityFrameworkCore;

namespace Arius.Web.Infrastructure;

public class RepositoryRepository : IRepositoryRepository
{
    private readonly ApplicationDbContext _context;

    public RepositoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Repository>> GetAllAsync()
    {
        return await _context.Repositories
            .Include(r => r.StorageAccount) // Include related StorageAccount
            .ToListAsync();
    }

    public async Task<Repository> GetByIdAsync(int id)
    {
        return await _context.Repositories
            .Include(r => r.StorageAccount) // Include related StorageAccount
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task AddAsync(Repository repository)
    {
        _context.Repositories.Add(repository);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Repository repository)
    {
        _context.Repositories.Update(repository);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var repository = await _context.Repositories.FindAsync(id);
        if (repository != null)
        {
            _context.Repositories.Remove(repository);
            await _context.SaveChangesAsync();
        }
    }
}