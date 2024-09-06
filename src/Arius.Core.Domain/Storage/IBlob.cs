namespace Arius.Core.Domain.Storage;

public interface IBlob
{
    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    string FullName { get; }

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    string Name { get; }

    //Task<long>                        GetContentLengthAsync();
    //Task<StorageTier>                 GetStorageTierAsync();
    //Task                              SetStorageTierAsync(StorageTier value);
    //Task<string?>                     GetContentTypeAsync();
    //Task                              SetContentTypeAsync(string value);
    //Task<IDictionary<string, string>> GetMetadataAsync();
    //Task<bool>                        ExistsAsync();
    //Task                              DeleteAsync();
    //Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
    //Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default, bool throwOnExists = true);
    //Task<Stream>                      OpenWriteAsync(bool throwOnExists = true);
    //Task<CopyFromUriOperation>        StartCopyFromUriAsync(Uri source, BlobCopyFromUriOptions options);
    //Uri                               Uri { get; }
}