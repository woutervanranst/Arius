using FluentResults;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Factory for creating storage-specific domain errors.
/// </summary>
internal static class StorageErrors
{
    /// <summary>
    /// Creates an error indicating that a blob was not found.
    /// </summary>
    /// <param name="blobName">The name of the blob that was not found</param>
    /// <returns>Error with BlobNotFound metadata</returns>
    public static Error BlobNotFound(string blobName) =>
        new Error($"Blob '{blobName}' was not found")
            .WithMetadata("ErrorCode", "BlobNotFound")
            .WithMetadata("BlobName", blobName);

    /// <summary>
    /// Creates an error indicating that a blob is archived and needs rehydration.
    /// </summary>
    /// <param name="blobName">The name of the archived blob</param>
    /// <returns>Error with BlobArchived metadata</returns>
    public static Error BlobArchived(string blobName) =>
        new Error($"Blob '{blobName}' is archived and requires rehydration before access")
            .WithMetadata("ErrorCode", "BlobArchived")
            .WithMetadata("BlobName", blobName);

    /// <summary>
    /// Creates an error indicating that a blob is currently being rehydrated.
    /// </summary>
    /// <param name="blobName">The name of the blob being rehydrated</param>
    /// <returns>Error with BlobRehydrating metadata</returns>
    public static Error BlobRehydrating(string blobName) =>
        new Error($"Blob '{blobName}' is currently being rehydrated from archive tier")
            .WithMetadata("ErrorCode", "BlobRehydrating")
            .WithMetadata("BlobName", blobName);
}