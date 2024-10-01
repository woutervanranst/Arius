using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Storage;

public interface IRemoteRepository
{
    public IRemoteStateRepository GetRemoteStateRepository();


    // Binary
    Task<BinaryProperties> UploadBinaryFileAsync(IBinaryFileWithHash file, Func<long, StorageTier> effectiveTier, CancellationToken cancellationToken = default);
    Task                   SetBinaryStorageTierAsync(Hash hash, StorageTier effectiveTier, CancellationToken cancellationToken = default);

    // General
    string ContainerName { get; }
    Task   DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default);
}