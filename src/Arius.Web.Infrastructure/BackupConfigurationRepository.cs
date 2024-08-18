using Arius.Web.Core;
using Microsoft.EntityFrameworkCore;

namespace Arius.Web.Infrastructure;

public class BackupConfigurationRepository : IBackupConfigurationRepository
{
    private readonly ApplicationDbContext _context;

    public BackupConfigurationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BackupConfiguration> GetByIdAsync(int id)
    {
        return await _context.BackupConfigurations.FindAsync(id);
    }

    public async Task<IEnumerable<BackupConfiguration>> GetAllAsync()
    {
        return await _context.BackupConfigurations.ToListAsync();
    }

    public async Task AddAsync(BackupConfiguration config)
    {
        _context.BackupConfigurations.Add(config);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(BackupConfiguration config)
    {
        _context.BackupConfigurations.Update(config);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var config = await _context.BackupConfigurations.FindAsync(id);
        if (config != null)
        {
            _context.BackupConfigurations.Remove(config);
            await _context.SaveChangesAsync();
        }
    }
}