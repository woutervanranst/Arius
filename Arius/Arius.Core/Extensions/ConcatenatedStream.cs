using System;
using System.Collections.Generic;
using System.IO;

namespace Arius.Core.Extensions;

internal class ConcatenatedStream : Stream
{
    public ConcatenatedStream(IEnumerable<Stream> streams)
    {
        this.streams = new Queue<Stream>(streams);
    }
    private readonly Queue<Stream> streams;

    public override bool CanRead => true;

    public override int Read(byte[] buffer, int offset, int count)
    {
        int totalBytesRead = 0;

        while (count > 0 && streams.Count > 0)
        {
            int bytesRead = streams.Peek().Read(buffer, offset, count);
            if (bytesRead == 0)
            {
                streams.Dequeue().Dispose();
                continue;
            }

            totalBytesRead += bytesRead;
            offset += bytesRead;
            count -= bytesRead;
        }

        return totalBytesRead;
    }

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override void Flush() => throw new NotImplementedException();

    public override long Length => throw new NotImplementedException();

    public override long Position
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

    public override void SetLength(long value) => throw new NotImplementedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
}