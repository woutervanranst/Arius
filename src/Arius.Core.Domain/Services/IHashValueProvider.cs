using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Services;

public interface IHashValueProvider
{
    Task<Hash> GetHashAsync(BinaryFile bf);
    bool       IsValid(Hash h);
}