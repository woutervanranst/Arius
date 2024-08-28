using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain.Repositories;

public interface IStateDbRepository
{
    RepositoryVersion                  Version { get; }
    IAsyncEnumerable<PointerFileEntry> GetPointerFileEntries();
    IAsyncEnumerable<string>           GetBinaryEntries();
    Task<bool>                         BinaryExistsAsync(Hash binaryFileHash);
}