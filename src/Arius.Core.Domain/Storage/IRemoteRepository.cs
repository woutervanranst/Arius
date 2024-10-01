using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Storage;

public interface IRemoteRepository
{
    // State
    IAsyncEnumerable<RepositoryVersion> GetStateDatabaseVersions();
    Task<RepositoryVersion?>            GetLatestStateDatabaseVersionAsync();
    IBlob                               GetStateDatabaseBlobForVersion(RepositoryVersion version);
    Task                                UploadStateDatabaseAsync(IStateDatabaseFile file, RepositoryVersion version, CancellationToken cancellationToken = default);

    // Binary
    Task<BinaryProperties> UploadBinaryFileAsync(IBinaryFileWithHash file, Func<long, StorageTier> effectiveTier, CancellationToken cancellationToken = default);
    Task                   SetBinaryStorageTierAsync(Hash hash, StorageTier effectiveTier, CancellationToken cancellationToken = default);

    // General
    Task DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default);
}