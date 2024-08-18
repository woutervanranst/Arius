namespace Arius.Web.Core;

public interface IBackupConfigurationRepository
{
    Task<BackupConfiguration>              GetByIdAsync(int id);
    Task<IEnumerable<BackupConfiguration>> GetAllAsync();
    Task                                   AddAsync(BackupConfiguration config);
    Task                                   UpdateAsync(BackupConfiguration config);
    Task                                   DeleteAsync(int id);
}