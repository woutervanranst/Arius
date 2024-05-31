using Arius.Core.Domain;
using Arius.Core.Interfaces;

namespace Arius.Core.Infrastucture;

public class BinaryRepository : IBinaryRepository
{
    public async Task<long> CountAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<bool> ExistsAsync(Hash hash)
    {
        throw new NotImplementedException();
    }

    public async Task<bool>     TryDownload(Hash hash, Stream stream, string passphrase)
    {
        throw new NotImplementedException();
    }
}