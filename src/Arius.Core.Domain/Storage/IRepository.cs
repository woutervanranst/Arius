namespace Arius.Core.Domain.Storage;

public interface IRepository
{
    IAsyncEnumerable<RepositoryVersion> GetRepositoryVersions();
    IBlob                               GetRepositoryVersionBlob(RepositoryVersion repositoryVersion);
    Task                                DownloadAsync(IBlob blob, string localPath, string passphrase, CancellationToken cancellationToken = default);
}