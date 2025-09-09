using Azure;

namespace Arius.Core.Shared.Storage;

public static class RequestFailedExceptionExtensions
{
    /// <summary>
    /// Determines if the exception indicates that the blob is in archive tier and needs to be rehydrated
    /// before it can be accessed.
    /// </summary>
    /// <param name="exception">The RequestFailedException to check</param>
    /// <returns>True if the blob is archived, false otherwise</returns>
    public static bool BlobIsArchived(this RequestFailedException exception)
    {
        return exception is { Status: 409, ErrorCode: "BlobArchived" };
    }

    /// <summary>
    /// Determines if the exception indicates that the blob is currently being rehydrated from archive tier
    /// and is not yet accessible.
    /// </summary>
    /// <param name="exception">The RequestFailedException to check</param>
    /// <returns>True if the blob is being rehydrated, false otherwise</returns>
    public static bool BlobIsRehydrating(this RequestFailedException exception)
    {
        return exception is { Status: 409, ErrorCode: "BlobBeingRehydrated" };
    }

    /// <summary>
    /// Determines if the exception indicates that the blob was not found (does not exist).
    /// </summary>
    /// <param name="exception">The RequestFailedException to check</param>
    /// <returns>True if the blob was not found, false otherwise</returns>
    public static bool BlobNotFound(this RequestFailedException exception)
    {
        return exception is { Status: 404, ErrorCode: "BlobNotFound" };
    }
}