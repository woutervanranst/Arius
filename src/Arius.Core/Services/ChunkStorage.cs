using Arius.Core.Extensions;
using Arius.Core.Models;
using System.IO.Compression;

namespace Arius.Core.Services;

internal class ChunkStorage : IChunkStorage
{
    private readonly IBlobStorage blobStorage;
    private readonly string       passphrase;

    private const string statesFolderPrefix = "states/";
    private const string chunksFolderPrefix = "chunks/";

    public ChunkStorage(IBlobStorage blobStorage, string passphrase)
    {
        this.blobStorage = blobStorage;
        this.passphrase  = passphrase;
    }

    // -- CONTAINER

    public Task<bool> CreateContainerIfNotExistsAsync()
    {
        return blobStorage.CreateContainerIfNotExistsAsync();
    }

    public Task<bool> ContainerExistsAsync()
    {
        return blobStorage.ContainerExistsAsync();
    }

    // -- STATES

    public IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default)
    {
        return blobStorage.GetBlobsAsync(statesFolderPrefix, cancellationToken)
            .OrderBy(blobName => blobName)
            .Select(blobName => blobName[statesFolderPrefix.Length..]); // remove the "states/" prefix
    }

    public async Task DownloadStateAsync(string stateName, FileInfo targetFile, CancellationToken cancellationToken = default)
    {
        var             blobName           = $"{statesFolderPrefix}{stateName}";
        await using var blobStream         = await blobStorage.OpenReadAsync(blobName, cancellationToken: cancellationToken);
        await using var decryptedStream    = await blobStream.GetDecryptionStreamAsync(passphrase, cancellationToken);
        await using var decompressedStream = new GZipStream(decryptedStream, CompressionMode.Decompress);
        await using var fileStream         = targetFile.Create();

        await decompressedStream.CopyToAsync(fileStream, cancellationToken);
    }

    public async Task UploadStateAsync(string stateName, FileInfo sourceFile, CancellationToken cancellationToken = default)
    {
        var blobName = $"{statesFolderPrefix}{stateName}";
        await using var blobStream = await blobStorage.OpenWriteAsync(
            blobName,
            throwOnExists: false,
            contentType: "application/aes256cbc+gzip",
            cancellationToken: cancellationToken);
        await using var encryptedStream  = await blobStream.GetCryptoStreamAsync(passphrase, cancellationToken);
        await using var compressedStream = new GZipStream(encryptedStream, CompressionLevel.Optimal);
        await using var fileStream       = sourceFile.OpenRead();

        await fileStream.CopyToAsync(compressedStream, cancellationToken);
    }

    // -- CHUNKS

    public async Task<Stream> OpenReadChunkAsync(Hash h, string passphrase, CancellationToken cancellationToken = default)
    {
        var blobName   = $"{chunksFolderPrefix}{h}";
        var blobStream = await blobStorage.OpenReadAsync(blobName, cancellationToken: cancellationToken);

        var decryptedStream = await blobStream.GetDecryptionStreamAsync(passphrase, cancellationToken);

        return new GZipStream(decryptedStream, CompressionMode.Decompress);
    }

    public async Task<Stream> OpenWriteChunkAsync(Hash h, string passphrase, CompressionLevel compressionLevel, string contentType, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default)
    {
        // Validate compression settings against content type to prevent double compression or missing compression
        ValidateCompressionSettings(compressionLevel, contentType);

        var blobName = $"{chunksFolderPrefix}{h}";

        var blobStream = await blobStorage.OpenWriteAsync(
            blobName,
            throwOnExists: false,
            metadata: metadata,
            contentType: contentType,
            progress: progress,
            cancellationToken: cancellationToken);

        var cryptoStream = await blobStream.GetCryptoStreamAsync(passphrase, cancellationToken);

        if (compressionLevel == CompressionLevel.NoCompression)
        {
            return new PositionTrackingStream(cryptoStream, blobStream);
        }
        else
        {
            var gzipStream = new GZipStream(cryptoStream, compressionLevel);
            return new PositionTrackingStream(gzipStream, blobStream);
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

        await blobStorage.SetAccessTierAsync(blobName, actualTier.ToAccessTier());

        return actualTier;

        static StorageTier GetActualStorageTier(StorageTier targetTier, long length)
        {
            const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

            if (targetTier == StorageTier.Archive && length <= oneMegaByte)
                targetTier = StorageTier.Cold; // Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive

            return targetTier;
        }
    }

    /// <summary>
    /// A stream wrapper that delegates write operations to one stream while reading position from another.
    /// This allows us to write through GZip/Crypto streams while tracking the actual bytes written to blob storage.
    /// </summary>
    private sealed class PositionTrackingStream : Stream
    {
        private readonly Stream writeStream;
        private readonly Stream positionStream;

        public PositionTrackingStream(Stream writeStream, Stream positionStream)
        {
            this.writeStream    = writeStream;
            this.positionStream = positionStream;
        }

        public override bool CanRead  => false;
        public override bool CanSeek  => false;
        public override bool CanWrite => writeStream.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => positionStream.Position;
            set => throw new NotSupportedException();
        }

        public override       void      Write(byte[] buffer, int offset, int count)                                            => writeStream.Write(buffer, offset, count);
        public override async Task      WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)  => await writeStream.WriteAsync(buffer, offset, count, cancellationToken);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => await writeStream.WriteAsync(buffer, cancellationToken);

        public override       void Flush()                                         => writeStream.Flush();
        public override async Task FlushAsync(CancellationToken cancellationToken) => await writeStream.FlushAsync(cancellationToken);

        public override int  Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)       => throw new NotSupportedException();
        public override void SetLength(long value)                      => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                writeStream?.Dispose();
                // positionStream is disposed through the writeStream chain (GZip→Crypto→Blob or Crypto→Blob)
            }
        }

        public override async ValueTask DisposeAsync()
        {
            if (writeStream != null)
                await writeStream.DisposeAsync();
            // positionStream is disposed through the writeStream chain (GZip→Crypto→Blob or Crypto→Blob)
        }
    }
}