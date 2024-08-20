namespace Arius.Core.Domain.Storage;

public interface IStorageAccount
{
    string AccountName { get; }
    string AccountKey  { get; }
    IAsyncEnumerable<IContainer> ListContainers(CancellationToken cancellationToken = default);
}