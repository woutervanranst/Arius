using Azure.Storage.Blobs.Models;

namespace Arius.Core.Shared.Storage;

public static class AzureBlobStorageExtensions
{
    public static StorageTier ToStorageTier(this AccessTier? tier)
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