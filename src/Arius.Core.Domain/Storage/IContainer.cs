namespace Arius.Core.Domain.Storage;

public interface IContainer
{
    IStorageAccount  StorageAccount { get; }
    string           Name           { get; }
    IRemoteRepository GetRemoteRepository(string passphrase);
}