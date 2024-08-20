namespace Arius.Core.Domain.Storage;

public interface IContainer
{
    string                    Name { get; }
    Task<IEnumerable<string>> ListBlobsAsync(CancellationToken cancellationToken = default); // Or a more complex Blob abstraction
}