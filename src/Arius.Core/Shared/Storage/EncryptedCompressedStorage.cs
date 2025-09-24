using Arius.Core.Shared.Crypto;
using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.Hashing;
using FluentResults;
using System.IO.Compression;
using Zio;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Implementation of IArchiveStorage that provides client-side AES256 encryption and compression
/// for chunk data before storing it in the underlying storage system.
/// </summary>
internal class EncryptedCompressedStorage : IArchiveStorage
{
    private readonly IRemoteStorageContainer container;
    private readonly string                  passphrase;

    private const string statesFolderPrefix           = "states/";
    private const string chunksFolderPrefix           = "chunks/";
    private const string rehydratedChunksFolderPrefix = "chunks-rehydrated/";

    public EncryptedCompressedStorage(IRemoteStorageContainer container, string passphrase)
    {
        this.container  = container;
        this.passphrase = passphrase;
    }

    // -- CONTAINER

    public Task<bool> CreateContainerIfNotExistsAsync()
    {
        return container.CreateContainerIfNotExistsAsync();
    }

    public Task<bool> ContainerExistsAsync()
    {
        return container.ContainerExistsAsync();
    }


    // -- STATES

    public IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default)
    {
        return container.GetAllAsync(statesFolderPrefix, cancellationToken)
            .OrderBy(sp => sp.Name)
            .Where(sp => sp.Metadata != null && sp.Metadata.TryGetValue("DatabaseVersion", out var version) && version == "5") // Get all v5 states
            .Select(sp => sp.Name[statesFolderPrefix.Length..]); // remove the "states/" prefix
    }

    public async Task DownloadStateAsync(string stateName, FileEntry targetFile, CancellationToken cancellationToken = default)
    {
        var blobName         = $"{statesFolderPrefix}{stateName}";
        var blobStreamResult = await container.OpenReadAsync(blobName, cancellationToken: cancellationToken);

        if (blobStreamResult.IsFailed)
        {
            var firstError = blobStreamResult.Errors.First();
            throw new InvalidOperationException($"Failed to download state '{stateName}': {firstError.Message}");
        }

        await using var blobStream         = blobStreamResult.Value;
        await using var decryptedStream    = await blobStream.GetDecryptionStreamAsync(passphrase, cancellationToken);
        await using var decompressedStream = new GZipStream(decryptedStream, CompressionMode.Decompress);

        try
        {
            await using var fileStream = targetFile.Create();
            await decompressedStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch (InvalidDataException e)
        {
            // TODO add a unit test for this case
            targetFile.Delete();
            throw new ArgumentException("Could not decrypt state file. Check the passphrase.");
        }
    }

    public async Task UploadStateAsync(string stateName, FileEntry sourceFile, CancellationToken cancellationToken = default)
    {
        var blobName         = $"{statesFolderPrefix}{stateName}";
        var blobStreamResult = await container.OpenWriteAsync(blobName, throwOnExists: false, contentType: "application/aes256cbc+gzip" /* TODO refactor me */, cancellationToken: cancellationToken);

        if (blobStreamResult.IsFailed)
            throw new InvalidOperationException($"Failed to open state blob for writing: {blobStreamResult.Errors.First()}");

        await using (var blobStream = blobStreamResult.Value)
        {
            await using var encryptedStream  = await blobStream.GetEncryptionStreamAsync(passphrase, cancellationToken);
            await using var compressedStream = new GZipStream(encryptedStream, CompressionLevel.SmallestSize);
            await using var fileStream       = sourceFile.Open(FileMode.Open, FileAccess.Read, FileShare.None);

            await fileStream.CopyToAsync(compressedStream, cancellationToken);
        }

        await container.SetAccessTierAsync(blobName, StorageTier.Cold);
        await container.SetMetadataAsync(blobName, new Dictionary<string, string> { { "DatabaseVersion", "5" } });
    }

    // -- CHUNKS

    public async Task<Result<Stream>> OpenReadChunkAsync(Hash h, CancellationToken cancellationToken = default)
    {
        return await OpenReadChunkInternalAsync(h, chunksFolderPrefix, cancellationToken);
    }

    public async Task<Result<Stream>> OpenReadHydratedChunkAsync(Hash h, CancellationToken cancellationToken = default)
    {
        return await OpenReadChunkInternalAsync(h, rehydratedChunksFolderPrefix, cancellationToken);
    }

    private async Task<Result<Stream>> OpenReadChunkInternalAsync(Hash h, string prefix, CancellationToken cancellationToken)
    {
        var blobName         = $"{prefix}{h}";
        var blobStreamResult = await container.OpenReadAsync(blobName, cancellationToken: cancellationToken);
        if (blobStreamResult.IsFailed)
            return blobStreamResult;

        // NOTE: do not use `await using` here, as we need to return the stream to the caller; the DisposableStreamWrapper takes care of proper disposal
        var blobStream      = blobStreamResult.Value;
        var decryptedStream = await blobStream.GetDecryptionStreamAsync(passphrase, cancellationToken);
        var gzipStream      = new GZipStream(decryptedStream, CompressionMode.Decompress);

        return Result.Ok<Stream>(new StreamWrapper(gzipStream, decryptedStream, blobStream));
    }

    public async Task<Result<Stream>> OpenWriteChunkAsync(Hash h, CompressionLevel compressionLevel, string contentType, IDictionary<string, string>? metadata = null, IProgress<long>? progress = null, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        // Validate compression settings against content type to prevent double compression or missing compression
        ValidateCompressionSettings(compressionLevel, contentType);

        var blobName         = $"{chunksFolderPrefix}{h}";
        var blobStreamResult = await container.OpenWriteAsync(blobName, throwOnExists: !overwrite, metadata: metadata, contentType: contentType, progress: progress, cancellationToken: cancellationToken);

        if (blobStreamResult.IsFailed)
            return blobStreamResult;

        var blobStream   = blobStreamResult.Value;
        var cryptoStream = await blobStream.GetEncryptionStreamAsync(passphrase, cancellationToken);

        if (compressionLevel == CompressionLevel.NoCompression)
        {
            return Result.Ok<Stream>(new StreamWrapper(innerStream: cryptoStream, positionStream: blobStream, disposables: [cryptoStream, blobStream]));
        }
        else
        {
            var gzipStream = new GZipStream(cryptoStream, compressionLevel);
            return Result.Ok<Stream>(new StreamWrapper(innerStream: gzipStream, positionStream: blobStream, disposables: [gzipStream, cryptoStream, blobStream]));
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

    public async Task<StorageProperties?> GetChunkPropertiesAsync(Hash h, CancellationToken cancellationToken = default)
    {
        var blobName = $"{chunksFolderPrefix}{h}";
        return await container.GetPropertiesAsync(blobName, cancellationToken);
    }

    public async Task DeleteChunkAsync(Hash h, CancellationToken cancellationToken = default)
    {
        var blobName = $"{chunksFolderPrefix}{h}";
        await container.DeleteAsync(blobName, cancellationToken);
    }

    public async Task SetChunkMetadataAsync(Hash h, IDictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        var blobName = $"{chunksFolderPrefix}{h}";
        await container.SetMetadataAsync(blobName, metadata, cancellationToken);
    }

    public async Task<StorageTier> SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier)
    {
        var actualTier = GetActualStorageTier(targetTier, length);
        var blobName   = $"{chunksFolderPrefix}{h}";

        await container.SetAccessTierAsync(blobName, actualTier);

        return actualTier;

        static StorageTier GetActualStorageTier(StorageTier targetTier, long length)
        {
            const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

            if (targetTier == StorageTier.Archive && length <= oneMegaByte)
                targetTier = StorageTier.Cold; // Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive

            return targetTier;
        }
    }

    public async Task StartHydrationAsync(Hash hash, RehydratePriority priority)
    {
        var source = $"{chunksFolderPrefix}{hash}";
        var target = $"{rehydratedChunksFolderPrefix}{hash}";

        await container.StartHydrationAsync(source, target, priority);
    }
}