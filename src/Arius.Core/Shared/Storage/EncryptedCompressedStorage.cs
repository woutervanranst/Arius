using Arius.Core.Shared.Crypto;
using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.Hashing;
using System.IO.Compression;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Implementation of IArchiveStorage that provides client-side AES256 encryption and compression
/// for chunk data before storing it in the underlying storage system.
/// </summary>
internal class EncryptedCompressedStorage : IArchiveStorage
{
    private readonly IStorage storage;
    private readonly string   passphrase;

    private const string statesFolderPrefix = "states/";
    private const string chunksFolderPrefix = "chunks/";

    public EncryptedCompressedStorage(IStorage storage, string passphrase)
    {
        this.storage    = storage;
        this.passphrase = passphrase;
    }

    // -- CONTAINER

    public Task<bool> CreateContainerIfNotExistsAsync()
    {
        return storage.CreateContainerIfNotExistsAsync();
    }

    public Task<bool> ContainerExistsAsync()
    {
        return storage.ContainerExistsAsync();
    }


    // -- STATES

    public IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default)
    {
        return storage.GetNamesAsync(statesFolderPrefix, cancellationToken)
            .OrderBy(blobName => blobName)
            .Select(blobName => blobName[statesFolderPrefix.Length..]); // remove the "states/" prefix
    }

    public async Task DownloadStateAsync(string stateName, FileInfo targetFile, CancellationToken cancellationToken = default)
    {
        var             blobName           = $"{statesFolderPrefix}{stateName}";
        await using var blobStream         = await storage.OpenReadAsync(blobName, cancellationToken: cancellationToken);
        await using var decryptedStream    = await blobStream.GetDecryptionStreamAsync(passphrase, cancellationToken);
        await using var decompressedStream = new GZipStream(decryptedStream, CompressionMode.Decompress);
        await using var fileStream         = targetFile.Create();

        await decompressedStream.CopyToAsync(fileStream, cancellationToken);
    }

    public async Task UploadStateAsync(string stateName, FileInfo sourceFile, CancellationToken cancellationToken = default)
    {
        var             blobName         = $"{statesFolderPrefix}{stateName}";
        await using var blobStream       = await storage.OpenWriteAsync(blobName, throwOnExists: false, contentType: "application/aes256cbc+gzip", cancellationToken: cancellationToken);
        await using var encryptedStream  = await blobStream.GetEncryptionStreamAsync(passphrase, cancellationToken);
        await using var compressedStream = new GZipStream(encryptedStream, CompressionLevel.Optimal);
        await using var fileStream       = sourceFile.OpenRead();

        await fileStream.CopyToAsync(compressedStream, cancellationToken);
    }


    // -- CHUNKS

    public async Task<Stream> OpenReadChunkAsync(Hash h, CancellationToken cancellationToken = default)
    {
        // NOTE: do not use `await using` here, as we need to return the stream to the caller; the DisposableStreamWrapper takes care of proper disposal
        var blobName        = $"{chunksFolderPrefix}{h}";
        var blobStream      = await storage.OpenReadAsync(blobName, cancellationToken: cancellationToken);
        var decryptedStream = await blobStream.GetDecryptionStreamAsync(passphrase, cancellationToken);
        var gzipStream      = new GZipStream(decryptedStream, CompressionMode.Decompress);

        return new StreamWrapper(gzipStream, decryptedStream, blobStream);
    }

    public async Task<Stream> OpenWriteChunkAsync(Hash h, CompressionLevel compressionLevel, string contentType, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default)
    {
        // Validate compression settings against content type to prevent double compression or missing compression
        ValidateCompressionSettings(compressionLevel, contentType);

        var blobName     = $"{chunksFolderPrefix}{h}";
        var blobStream   = await storage.OpenWriteAsync(blobName, throwOnExists: false, metadata: metadata, contentType: contentType, progress: progress, cancellationToken: cancellationToken);
        var cryptoStream = await blobStream.GetEncryptionStreamAsync(passphrase, cancellationToken);

        if (compressionLevel == CompressionLevel.NoCompression)
        {
            return new StreamWrapper(innerStream: cryptoStream, positionStream: blobStream, disposables: [cryptoStream, blobStream]);
        }
        else
        {
            var gzipStream = new GZipStream(cryptoStream, compressionLevel);
            return new StreamWrapper(innerStream: gzipStream, positionStream: blobStream, disposables: [gzipStream, cryptoStream, blobStream]);
        }

        static void ValidateCompressionSettings(CompressionLevel compressionLevel, string contentType)
        {
            var isAlreadyCompressed = contentType.Contains("tar+gzip", StringComparison.OrdinalIgnoreCase);
            var isCompressing       = compressionLevel != CompressionLevel.NoCompression;

            if (isAlreadyCompressed && isCompressing)
            {
                throw new InvalidOperationException($"Content type '{contentType}' indicates pre-compressed data, but compression level is set to '{compressionLevel}'. Use CompressionLevel.NoCompression to avoid double compression.");
            }
            else if (!isAlreadyCompressed && !isCompressing)
            {
                throw new InvalidOperationException($"Content type '{contentType}' indicates uncompressed data, but compression level is set to '{compressionLevel}'. Consider using CompressionLevel.Optimal for better storage efficiency.");
            }
        }
    }

    public async Task<StorageTier> SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier)
    {
        var actualTier = GetActualStorageTier(targetTier, length);
        var blobName   = $"{chunksFolderPrefix}{h}";

        await storage.SetAccessTierAsync(blobName, actualTier.ToAccessTier());

        return actualTier;

        static StorageTier GetActualStorageTier(StorageTier targetTier, long length)
        {
            const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

            if (targetTier == StorageTier.Archive && length <= oneMegaByte)
                targetTier = StorageTier.Cold; // Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive

            return targetTier;
        }
    }

}