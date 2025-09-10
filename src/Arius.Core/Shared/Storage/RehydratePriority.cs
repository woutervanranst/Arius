namespace Arius.Core.Shared.Storage;

using AzureRehydratePriority = Azure.Storage.Blobs.Models.RehydratePriority;
using AriusRehydratePriority = RehydratePriority;

internal enum RehydratePriority
{
    Standard,
    High
}

internal static class RehydratePriorityExtensions
{
    public static AzureRehydratePriority ToRehydratePriority(this AriusRehydratePriority storageTier)
    {
        return storageTier switch
        {
            AriusRehydratePriority.Standard => AzureRehydratePriority.Standard ,
            AriusRehydratePriority.High     => AzureRehydratePriority.High,
            _                            => throw new ArgumentOutOfRangeException(nameof(storageTier), storageTier, null)
        };
    }
}