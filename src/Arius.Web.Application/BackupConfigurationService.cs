using Arius.Web.Core;

namespace Arius.Web.Application;

public class BackupConfigurationService
{
    private readonly IBackupConfigurationRepository _repository;

    public BackupConfigurationService(IBackupConfigurationRepository repository)
    {
        _repository = repository;
    }

    public Task<BackupConfiguration>              GetBackupConfigurationAsync(int id)                        => _repository.GetByIdAsync(id);
    public Task<IEnumerable<BackupConfiguration>> GetBackupConfigurationsAsync()                             => _repository.GetAllAsync();
    public Task                                   AddBackupConfigurationAsync(BackupConfiguration config)    => _repository.AddAsync(config);
    public Task                                   UpdateBackupConfigurationAsync(BackupConfiguration config) => _repository.UpdateAsync(config);
    public Task                                   DeleteBackupConfigurationAsync(int id)                     => _repository.DeleteAsync(id);
}