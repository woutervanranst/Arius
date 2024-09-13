using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Storage;

public interface IRepository
{
    IAsyncEnumerable<RepositoryVersion> GetRepositoryVersions();
    IBlob                               GetRepositoryVersionBlob(RepositoryVersion repositoryVersion);
    Task<BinaryProperties>              UploadBinaryFileAsync(IBinaryFileWithHash file, Func<long, StorageTier> effectiveTier, CancellationToken cancellationToken = default);
    Task                                DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default);
    Task                                SetChunkStorageTierAsync(Hash hash, StorageTier effectiveTier, CancellationToken cancellationToken = default);
}