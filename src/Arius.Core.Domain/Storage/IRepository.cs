using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Storage;

public interface IRepository
{
    IAsyncEnumerable<RepositoryVersion> GetRepositoryVersions();
    IBlob                               GetRepositoryVersionBlob(RepositoryVersion repositoryVersion);
    Task<BinaryProperties>              UploadChunkAsync(IBinaryFileWithHash file, CancellationToken cancellationToken = default);
    Task                                DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default);
}