using Arius.Core.Shared.FileSystem;
using System.Formats.Tar;
using System.IO.Compression;

namespace Arius.Core.Features.Archive;

internal sealed class InMemoryGzippedTarWriter : IDisposable
{
    private readonly MemoryStream memoryStream;
    private readonly GZipStream   gzipStream;
    private readonly TarWriter    tarWriter;
    private          bool         disposed = false;

    public long Position => memoryStream.Position;

    public InMemoryGzippedTarWriter(CompressionLevel compressionLevel)
    {
        memoryStream = new MemoryStream();
        gzipStream   = new GZipStream(memoryStream, compressionLevel, leaveOpen: true);
        tarWriter    = new TarWriter(gzipStream);
    }

    public async Task<long> AddEntryAsync(BinaryFile binaryFile, string entryName, CancellationToken cancellationToken = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(InMemoryGzippedTarWriter));

        var previousPosition = memoryStream.Position;

        await using (var ss = binaryFile.OpenRead())
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
            {
                DataStream = ss
            };

            await tarWriter.WriteEntryAsync(entry, cancellationToken);
        }

        await gzipStream.FlushAsync(cancellationToken);
        await memoryStream.FlushAsync(cancellationToken);

        return memoryStream.Position - previousPosition;
    }

    public Stream GetCompletedArchive()
    {
        if (disposed) throw new ObjectDisposedException(nameof(InMemoryGzippedTarWriter));

        tarWriter.Dispose();
        gzipStream.Dispose();
        memoryStream.Seek(0, SeekOrigin.Begin);

        return memoryStream;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            tarWriter?.Dispose();
            gzipStream?.Dispose();
            memoryStream?.Dispose();
            disposed = true;
        }
    }
}