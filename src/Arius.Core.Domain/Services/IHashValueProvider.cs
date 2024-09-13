using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Services;

public interface IHashValueProvider
{
    Task<Hash> GetHashAsync(IBinaryFile bf);
    bool       IsValid(Hash h);
}