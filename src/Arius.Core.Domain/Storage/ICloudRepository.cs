using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Storage;

public interface ICloudRepository
{
    IAsyncEnumerable<RepositoryVersion> GetStateDatabaseVersions();
    IBlob                               GetStateDatabaseBlobForVersion(RepositoryVersion repositoryVersion);
    Task<BinaryProperties>              UploadBinaryFileAsync(IBinaryFileWithHash file, Func<long, StorageTier> effectiveTier, CancellationToken cancellationToken = default);
    Task                                SetChunkStorageTierAsync(Hash hash, StorageTier effectiveTier, CancellationToken cancellationToken = default);
    Task                                DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default);
}