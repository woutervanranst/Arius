namespace Arius.Core.Domain.Storage;

public interface IStorageAccount
{
    //string AccountName { get; }
    //string AccountKey  { get; }
    IContainer GetContainer(string containerName);
    IAsyncEnumerable<IContainer> GetContainers(CancellationToken cancellationToken = default);
}