using Arius.Core.Extensions;
using Arius.Core.Models;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System.IO.Compression;
using System.Net;

namespace Arius.Core.Services;

internal class BlobStorage : IBlobStorage
{
    private readonly string              connectionString;
    private readonly BlobServiceClient   blobServiceClient;
    private readonly BlobContainerClient blobContainerClient;

    public BlobStorage(string accountName, string accountKey, string containerName)
    {
        connectionString    = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
        blobServiceClient   = new BlobServiceClient(connectionString);
        blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    /// <summary>
    /// Create Blob Container if it does not exist
    /// </summary>
    /// <returns>True if it was created</returns>
    public async Task<bool> CreateContainerIfNotExistsAsync()
    {
        try
        {
            var r = await blobContainerClient.CreateIfNotExistsAsync(PublicAccessType.None);
            return r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created;
        }
        catch (RequestFailedException e)
        {
            throw new InvalidOperationException($"Failed to create or access Azure Storage container '{blobContainerClient.Name}'. Please check your account credentials and permissions. See the log file for detailed error information.", e);
        }
    }

    public async Task<bool> ContainerExistsAsync()
    {
        try
        {
            return await blobContainerClient.ExistsAsync();
        }
        catch (RequestFailedException e)
        {
            throw new InvalidOperationException($"Failed to access Azure Storage container '{blobContainerClient.Name}'. Please check your account credentials and permissions. See the log file for detailed error information.", e);
        }
    }


    // --- STATES

    private const string statesFolderPrefix = "states/";

    /// <summary>
    /// Get an ordered list of state names in the specified container.
    /// </summary>
    /// <returns></returns>
    public IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default)
    {
        return blobContainerClient.GetBlobsAsync(prefix: statesFolderPrefix, cancellationToken: cancellationToken)
            .OrderBy(b => b.Name)
            .Select(b => b.Name[statesFolderPrefix.Length ..]); // remove the "states/" prefix
    }

    public async Task DownloadStateAsync(string stateName, FileInfo targetFile, CancellationToken cancellationToken = default)
    {
        var blobClient = blobContainerClient.GetBlobClient($"{statesFolderPrefix}{stateName}");
        await blobClient.DownloadToAsync(targetFile.FullName, cancellationToken);
    }

    public async Task UploadStateAsync(string stateName, FileInfo sourceFile, CancellationToken cancellationToken = default)
    {
        var blobClient = blobContainerClient.GetBlobClient($"{statesFolderPrefix}{stateName}");
        await blobClient.UploadAsync(sourceFile.FullName, overwrite: true, cancellationToken);
    }


    // --- CHUNKS

    private const string chunksFolderPrefix = "chunks/";

    public async Task<Stream> OpenReadChunkAsync(Hash h, string passphrase, CancellationToken cancellationToken = default)
    {
        var bbc = blobContainerClient.GetBlockBlobClient($"{chunksFolderPrefix}{h}");
        var blobStream = await bbc.OpenReadAsync(cancellationToken: cancellationToken);
        
        var decryptedStream = await blobStream.GetDecryptionStreamAsync(passphrase, cancellationToken);
        
        return new GZipStream(decryptedStream, CompressionMode.Decompress);
    }

    public async Task<Stream> OpenWriteChunkAsync(Hash h, string passphrase, CompressionLevel compressionLevel, string contentType, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default)
    {
        // Validate compression settings against content type to prevent double compression or missing compression
        ValidateCompressionSettings(compressionLevel, contentType);
        
        var bbc = blobContainerClient.GetBlockBlobClient($"{chunksFolderPrefix}{h}");

        var bbowo = new BlockBlobOpenWriteOptions();

        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        var throwOnExists = false;
        if (throwOnExists)
            bbowo.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }; // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
        if (metadata is not null)
            bbowo.Metadata = metadata;
        bbowo.HttpHeaders     = new BlobHttpHeaders { ContentType = contentType };
        bbowo.ProgressHandler = progress;

        var blobStream = await bbc.OpenWriteAsync(overwrite: true, options: bbowo, cancellationToken: cancellationToken);
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
                throw new ArgumentException($"Content type '{contentType}' indicates pre-compressed data, but compression level is set to '{compressionLevel}'. Use CompressionLevel.NoCompression to avoid double compression.", nameof(compressionLevel));
            }

            if (!isAlreadyCompressed && !isCompressing)
            {
                throw new ArgumentException($"Content type '{contentType}' indicates uncompressed data, but compression level is set to '{compressionLevel}'. Consider using CompressionLevel.Optimal for better storage efficiency.", nameof(compressionLevel));
            }
        }
    }

    public async Task<StorageTier> SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier)
    {
        var actualTier = GetActualStorageTier(targetTier, length);
        var bbc        = blobContainerClient.GetBlobClient($"{chunksFolderPrefix}{h}");

        await bbc.SetAccessTierAsync(actualTier.ToAccessTier());

        return actualTier;


        static StorageTier GetActualStorageTier(StorageTier targetTier, long length)
        {
            const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

            if (targetTier == StorageTier.Archive && length <= oneMegaByte)
                targetTier = StorageTier.Cold; //Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive

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
            this.writeStream = writeStream;
            this.positionStream = positionStream;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => writeStream.CanWrite;

        public override long Length => throw new NotSupportedException();
        public override long Position 
        { 
            get => positionStream.Position; 
            set => throw new NotSupportedException(); 
        }

        public override void Write(byte[] buffer, int offset, int count) => writeStream.Write(buffer, offset, count);
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => await writeStream.WriteAsync(buffer, offset, count, cancellationToken);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => await writeStream.WriteAsync(buffer, cancellationToken);

        public override void Flush() => writeStream.Flush();
        public override async Task FlushAsync(CancellationToken cancellationToken) => await writeStream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

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
