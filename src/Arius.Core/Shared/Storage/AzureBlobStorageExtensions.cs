using Azure.Storage.Blobs.Models;

namespace Arius.Core.Shared.Storage;

internal static class AzureBlobStorageExtensions
{
    extension(AccessTier? tier)
    {
        public StorageTier ToStorageTier()
        {
            if (tier == null)
                throw new ArgumentOutOfRangeException();

            if (tier == AccessTier.Hot)
                return StorageTier.Hot;

            if (tier == AccessTier.Cool)
                return StorageTier.Cool;

            if (tier == AccessTier.Cold)
                return StorageTier.Cold;

            if (tier == AccessTier.Archive)
                return StorageTier.Archive;

            throw new ArgumentOutOfRangeException();
        }
    }
}