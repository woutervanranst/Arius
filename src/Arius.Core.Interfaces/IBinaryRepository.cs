using Arius.Core.Domain;

namespace Arius.Core.Interfaces
{
    public interface IBinaryRepository
    {
        Task<long> CountAsync();

        Task<bool> ExistsAsync(Hash hash);

        Task<bool> TryDownload(Hash hash, Stream stream, string passphrase);
    }
}
