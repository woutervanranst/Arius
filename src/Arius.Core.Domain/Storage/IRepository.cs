namespace Arius.Core.Domain.Storage;

public interface IRepository
{
    //IContainer                Container  { get; }
    //Task<IEnumerable<string>> ListBlobsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<RepositoryVersion> GetRepositoryVersions();
}