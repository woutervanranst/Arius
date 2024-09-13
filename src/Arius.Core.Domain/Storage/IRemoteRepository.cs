using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Storage;

public interface IRemoteRepository
{
    IAsyncEnumerable<RepositoryVersion> GetStateDatabaseVersions();
    IBlob                               GetStateDatabaseBlobForVersion(RepositoryVersion repositoryVersion);
    Task                                UploadStateDatabaseAsync(ILocalStateRepository localStateRepository, IBlob blob);
    Task<BinaryProperties>              UploadBinaryFileAsync(IBinaryFileWithHash file, Func<long, StorageTier> effectiveTier, CancellationToken cancellationToken = default);
    Task                                SetChunkStorageTierAsync(Hash hash, StorageTier effectiveTier, CancellationToken cancellationToken = default);
    Task                                DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default);
}