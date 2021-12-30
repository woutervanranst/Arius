using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Services.Chunkers;

internal class ByteBoundaryChunker : Chunker
{
    public ByteBoundaryChunker(ILogger<ByteBoundaryChunker> logger, 
        IHashValueProvider hashValueProvider, 
        int bufferSize = 1024 * 24,   // with an average chunk size of 14 KB at 4 KB min size, setting this sufficiently high enough to minimize allocations
        int minChunkSize = 1024 * 4)  // 4 KB minimum (i.o. 1 KB) has better performance characteristics (empirically tested)
        : base(hashValueProvider)
    {
        this.logger = logger;
        this.bufferSize = bufferSize;
        MinChunkSize = minChunkSize;
        Delimiter = new byte[] { 0, 0 };
    }

    public byte[] Delimiter { get; }
    public int MinChunkSize { get; }


    private readonly ILogger<ByteBoundaryChunker> logger;
    private readonly int bufferSize;

    // ReadOnlySequence<T>.AsStream() -- see https://github.com/AArnott/Nerdbank.Streams/blob/main/doc/AsStream.md#readonlysequencebyte, may come native in .NET 6 https://github.com/dotnet/runtime/issues/27156
    // Good intro on Buffers: https://docs.microsoft.com/en-us/dotnet/standard/io/buffers#readonlysequencet

    public override IEnumerable<MemoryChunk> Chunk(Stream stream)
    {
        // https://keestalkstech.com/2010/11/seek-position-of-a-string-in-a-file-or-filestream/

        var buffer = new byte[bufferSize];
        var size = bufferSize;
        var offset = 0;
        var position = stream.Position;
        var subChunks = new List<byte[]>(); //moving away from the Array.Empty<byte>() + Concat algo because, on average, every byte[] gets copied 5 times resulting in a lot of garbage collection

        while (true)
        {
            var bytesRead = stream.Read(buffer, offset, size);

            // when no bytes are read -- the string could not be found
            if (bytesRead <= 0)
                break;

            // when less then size bytes are read, we need to slice the buffer to prevent reading of "previous" bytes
            ReadOnlySpan<byte> ro = buffer;
            if (bytesRead < size)
                ro = ro.Slice(0, offset + bytesRead /*size*/);

            // TODO Optimize the creation of chunk by already taking the MinChunkSize: chunkBytes[MinChunkSize..] and then looking after that

            // check if we can find our search bytes in the buffer
            var i = ro.IndexOf(Delimiter); //NOTE: this has very low complexity vs reading byte per byte, ref https://stackoverflow.com/questions/51864673/c-sharp-readonlyspanchar-vs-substring-for-string-dissection
            if (i > -1 &&  // we found something
                i <= bytesRead &&  //i <= r  -- we found something in the area that was read (at the end of the buffer, the last values are not overwritten). i = r if the delimiter is at the end of the buffer
                subChunks.Sum(sc => sc.Length) + (i + Delimiter.Length - offset) >= MinChunkSize)  //the size of the chunk that will be made is large enough
            {
                subChunks.Add(buffer[offset..(i + Delimiter.Length)]);
                var chunk = GetChunk(subChunks);
                var ch = hashValueProvider.GetChunkHash(chunk);

                yield return new MemoryChunk(chunk, ch);

                subChunks.Clear();

                offset = 0;
                size = bufferSize;
                position += i + Delimiter.Length;
                stream.Position = position;
                continue;
            }
            else if (stream.Position == stream.Length)
            {
                // we re at the end of the stream
                subChunks.Add(buffer[offset..(bytesRead + offset)]); //return the bytes read
                var chunk = GetChunk(subChunks);
                var ch = hashValueProvider.GetChunkHash(chunk);

                yield return new MemoryChunk(chunk, ch);

                break;
            }

            // the stream is not finished. Copy the last 2 bytes to the beginning of the buffer and set the offset to fill the buffer as of byte 3
            subChunks.Add(buffer[offset..buffer.Length]);

            offset = Delimiter.Length;
            size = bufferSize - offset;
            Array.Copy(buffer, buffer.Length - offset, buffer, 0, offset);
            position += bufferSize - offset;
        }
    }

    private static byte[] GetChunk(List<byte[]> subChunks)
    {
        byte[] chunk = new byte[subChunks.Sum(sc => sc.Length)];

        int destinationIndex = 0;

        foreach (var subChunk in subChunks)
        {
            Array.Copy(subChunk, 0, chunk, destinationIndex, subChunk.Length);
            destinationIndex += subChunk.Length;
        }

        return chunk;
    }
}