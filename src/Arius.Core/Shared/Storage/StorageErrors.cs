using FluentResults;

namespace Arius.Core.Shared.Storage;

internal abstract class StorageError(string message) : Error(message);

internal sealed class BlobNotFoundError : StorageError
{
    public BlobNotFoundError(string blobName)
        : base($"Blob '{blobName}' was not found") 
        => BlobName = blobName;

    public string BlobName { get; }
}

internal sealed class BlobArchivedError : StorageError
{
    public BlobArchivedError(string blobName) 
        : base($"Blob '{blobName}' is archived and requires rehydration before access") 
        => BlobName = blobName;

    public string BlobName { get; }
}

internal sealed class BlobRehydratingError : StorageError
{
    public BlobRehydratingError(string blobName)
        : base($"Blob '{blobName}' is currently being rehydrated from archive tier")
        => BlobName = blobName;

    public string BlobName { get; }
}

internal sealed class BlobAlreadyExistsError : StorageError
{
    public BlobAlreadyExistsError(string blobName)
        : base($"Blob '{blobName}' already exists")
        => BlobName = blobName;

    public string BlobName { get; }
}