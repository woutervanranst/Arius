namespace Arius.Core.Shared.Storage;

/// <summary>
/// Content type constants for Azure Blob Storage.
/// </summary>
internal static class ChunkContentTypes
{
    /// <summary>
    /// Content type for encrypted and compressed chunk data.
    /// </summary>
    public const string ChunkContentType = "application/aes256cbc+gzip";

    /// <summary>
    /// Content type for encrypted, tar-archived, and compressed chunk data.
    /// </summary>
    public const string TarChunkContentType = "application/aes256cbc+tar+gzip";
}
