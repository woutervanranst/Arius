namespace Arius.Core.Domain.Storage;

public interface IStorageAccount
{
    string                       AccountName { get; }
    IAsyncEnumerable<IContainer> ListContainersAsync(CancellationToken cancellationToken = default);
}