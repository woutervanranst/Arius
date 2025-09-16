using Azure.Storage.Blobs.Models;

namespace Arius.Core.Shared.Storage;

public enum StorageTier
{
    Hot,
    Cool,
    Cold,
    Archive
}

internal static class StorageTierExtensions
{
    public static StorageTier ToStorageTier(this AccessTier accessTier)
    {
        if (accessTier == AccessTier.Hot)
            return StorageTier.Hot;
        if (accessTier == AccessTier.Cool)
            return StorageTier.Cool;
        if (accessTier == AccessTier.Cold)
            return StorageTier.Cold;
        if (accessTier == AccessTier.Archive)
            return StorageTier.Archive;

        throw new ArgumentException("Unknown AccessTier");
    }

    public static StorageTier ToStorageTier(this string accessTier)
    {
        return accessTier.ToLowerInvariant() switch
        {
            "hot"     => StorageTier.Hot,
            "cool"    => StorageTier.Cool,
            "cold"    => StorageTier.Cold,
            "archive" => StorageTier.Archive,
            _         => throw new ArgumentException($"Unknown access tier: {accessTier}")
        };
    }


    public static AccessTier ToAccessTier(this StorageTier storageTier)
    {
        return storageTier switch
        {
            StorageTier.Hot     => AccessTier.Hot,
            StorageTier.Cool    => AccessTier.Cool,
            StorageTier.Cold    => AccessTier.Cold,
            StorageTier.Archive => AccessTier.Archive,
            _                   => throw new ArgumentException("Unknown StorageTier")
        };
    }

    //public static bool IsOnlineTier(this StorageTier? storageTier) => storageTier != StorageTier.Archive;
}