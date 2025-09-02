namespace Arius.Core.Extensions;

/// <summary>
/// A stream wrapper that disposes multiple IDisposable objects when disposed itself (in this case, the AES).
/// </summary>
internal sealed class DisposableStreamWrapper : Stream
{
    private readonly Stream        _innerStream;
    private readonly IDisposable[] _disposables;

    public DisposableStreamWrapper(Stream innerStream, params IDisposable[] disposables)
    {
        _innerStream = innerStream;
        _disposables = disposables;
    }

    public override bool CanRead  => _innerStream.CanRead;
    public override bool CanSeek  => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length   => _innerStream.Length;

    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush()                                         => _innerStream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _innerStream.FlushAsync(cancellationToken);
    public override int  Read(byte[] buffer, int offset, int count)      => _innerStream.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin)            => _innerStream.Seek(offset, origin);
    public override void SetLength(long value)                           => _innerStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count)     => _innerStream.Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _innerStream.WriteAsync(buffer, offset, count, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerStream.Dispose();
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}