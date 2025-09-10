using Arius.Core.Features.Restore;

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
    public static AriusRehydratePriority ToRehydratePriority(this RehydrationDecision decision)
    {
        return decision switch
        {
            RehydrationDecision.StandardPriority => RehydratePriority.Standard ,
            RehydrationDecision.HighPriority     => RehydratePriority.High,
            _                                    => throw new ArgumentOutOfRangeException(nameof(decision), decision, null)
        };
    }

    public static AzureRehydratePriority ToRehydratePriority(this AriusRehydratePriority prio)
    {
        return prio switch
        {
            AriusRehydratePriority.Standard => AzureRehydratePriority.Standard,
            AriusRehydratePriority.High     => AzureRehydratePriority.High,
            _                               => throw new ArgumentOutOfRangeException(nameof(prio), prio, null)
        };
    }
}