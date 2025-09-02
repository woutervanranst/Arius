namespace Arius.Core.Extensions;

/// <summary>
/// A flexible stream wrapper that:
/// 1. Disposes multiple IDisposable objects when disposed itself
/// 2. Optionally tracks position from a different stream (for to know how large the compressed/encrypted blob is when written)
/// 3. Supports both sync and async disposal
/// </summary>
internal sealed class StreamWrapper : Stream
{
    private readonly Stream        innerStream;
    private readonly Stream?       positionStream;
    private readonly IDisposable[] disposables;

    /// <summary>
    /// Creates a wrapper that delegates all operations to the inner stream and disposes additional disposables.
    /// </summary>
    public StreamWrapper(Stream innerStream, params IDisposable[] disposables)
        : this(innerStream, null, disposables)
    {
    }

    /// <summary>
    /// Creates a wrapper that delegates operations to the inner stream but tracks position from a different stream.
    /// This is useful for write operations where you want to track bytes written to the underlying blob storage
    /// while writing through compression/encryption streams.
    /// </summary>
    public StreamWrapper(Stream innerStream, Stream? positionStream, params IDisposable[] disposables)
    {
        this.innerStream    = innerStream;
        this.positionStream = positionStream;
        this.disposables    = disposables;
    }

    public override bool CanRead  => innerStream.CanRead;
    public override bool CanSeek  => innerStream.CanSeek;
    public override bool CanWrite => innerStream.CanWrite;
    public override long Length   => innerStream.Length;

    public override long Position
    {
        get => positionStream?.Position ?? innerStream.Position;
        set => (positionStream ?? innerStream).Position = value;
    }

    public override void      Flush()                                                                                => innerStream.Flush();
    public override Task      FlushAsync(CancellationToken cancellationToken)                                        => innerStream.FlushAsync(cancellationToken);
    public override int       Read(byte[] buffer, int offset, int count)                                             => innerStream.Read(buffer, offset, count);
    public override long      Seek(long offset, SeekOrigin origin)                                                   => innerStream.Seek(offset, origin);
    public override void      SetLength(long value)                                                                  => innerStream.SetLength(value);
    public override void      Write(byte[] buffer, int offset, int count)                                            => innerStream.Write(buffer, offset, count);
    public override Task      WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)  => innerStream.WriteAsync(buffer, offset, count, cancellationToken);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => innerStream.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            innerStream.Dispose();
            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await innerStream.DisposeAsync();
        foreach (var disposable in disposables)
        {
            if (disposable is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                disposable.Dispose();
        }
    }
}