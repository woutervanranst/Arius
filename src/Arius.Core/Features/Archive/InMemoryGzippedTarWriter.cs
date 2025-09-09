using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using System.Formats.Tar;
using System.IO.Compression;

namespace Arius.Core.Features.Archive;

internal sealed class InMemoryGzippedTarWriter : IDisposable
{
    private readonly MemoryStream memoryStream;
    private readonly GZipStream   gzipStream;
    private readonly TarWriter    tarWriter;
    private readonly List<TarredEntry> tarredEntries = new();
    private          long         totalOriginalSize = 0;
    private          bool         disposed = false;

    public long Position => memoryStream.Position;
    public IReadOnlyList<TarredEntry> TarredEntries => tarredEntries;
    public long TotalOriginalSize => totalOriginalSize;

    public record TarredEntry(FilePair FilePair, Hash Hash, long ArchivedSize);

    public InMemoryGzippedTarWriter(CompressionLevel compressionLevel)
    {
        memoryStream = new MemoryStream();
        gzipStream   = new GZipStream(memoryStream, compressionLevel, leaveOpen: true);
        tarWriter    = new TarWriter(gzipStream);
    }

    public async Task<TarredEntry> AddEntryAsync(FilePair filePair, Hash hash, CancellationToken cancellationToken = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(InMemoryGzippedTarWriter));

        var previousPosition = memoryStream.Position;

        await using (var ss = filePair.BinaryFile.OpenRead())
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, hash.ToString())
            {
                DataStream = ss
            };

            await tarWriter.WriteEntryAsync(entry, cancellationToken);
        }

        await gzipStream.FlushAsync(cancellationToken);
        await memoryStream.FlushAsync(cancellationToken);

        var archivedSize = memoryStream.Position - previousPosition;
        
        // Track the entry
        var tarredEntry = new TarredEntry(filePair, hash, archivedSize);
        tarredEntries.Add(tarredEntry);
        totalOriginalSize += filePair.BinaryFile.Length;

        return tarredEntry;
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