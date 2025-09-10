namespace Arius.Core.Shared.Storage;

public enum RehydrationPriority
{
    Standard,
    High
}

internal static class RehydrationPriorityExtensions
{
    public static Azure.Storage.Blobs.Models.RehydratePriority ToRehydratePriority(this RehydrationPriority storageTier)
    {
        return storageTier switch
        {
            RehydrationPriority.Standard => Azure.Storage.Blobs.Models.RehydratePriority.Standard ,
            RehydrationPriority.High     => Azure.Storage.Blobs.Models.RehydratePriority.High,
            _                            => throw new ArgumentOutOfRangeException(nameof(storageTier), storageTier, null)
        };
    }
}