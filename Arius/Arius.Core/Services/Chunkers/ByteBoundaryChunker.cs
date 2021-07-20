using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.HighPerformance;
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

namespace Arius.Core.Services.Chunkers
{
    internal class ByteBoundaryChunker : Chunker
    {
        public ByteBoundaryChunker(ILogger<ByteBoundaryChunker> logger, IHashValueProvider hashValueProvider, int bufferSize = 8192, int minChunkSize = 1024) : base(hashValueProvider)
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

        public override IEnumerable<ByteArrayChunk> Chunk(Stream stream)
        {
            // https://keestalkstech.com/2010/11/seek-position-of-a-string-in-a-file-or-filestream/

            var buffer = new byte[bufferSize];
            var size = bufferSize;
            var offset = 0;
            var position = stream.Position;
            var nextChunk = Array.Empty<byte>();

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

                // check if we can find our search bytes in the buffer
                var i = ro.IndexOf(Delimiter);
                if (i > -1 &&  // we found something
                    i <= bytesRead &&  //i <= r  -- we found something in the area that was read (at the end of the buffer, the last values are not overwritten). i = r if the delimiter is at the end of the buffer
                    nextChunk.Length + (i + Delimiter.Length - offset) >= MinChunkSize)  //the size of the chunk that will be made is large enough
                {
                    var chunk = buffer[offset..(i + Delimiter.Length)];
                    chunk = Concat(nextChunk, chunk);
                    var ch = hashValueProvider.GetChunkHash(chunk);

                    yield return new ByteArrayChunk(chunk, ch);

                    nextChunk = Array.Empty<byte>();

                    offset = 0;
                    size = bufferSize;
                    position += i + Delimiter.Length;
                    stream.Position = position;
                    continue;
                }
                else if (stream.Position == stream.Length)
                {
                    // we re at the end of the stream
                    var chunk = buffer[offset..(bytesRead + offset)]; //return the bytes read
                    chunk = Concat(nextChunk, chunk);
                    var ch = hashValueProvider.GetChunkHash(chunk);

                    yield return new ByteArrayChunk(chunk, ch);

                    break;
                }

                // the stream is not finished. Copy the last 2 bytes to the beginning of the buffer and set the offset to fill the buffer as of byte 3
                nextChunk = Concat(nextChunk, buffer[offset..buffer.Length]);

                offset = Delimiter.Length;
                size = bufferSize - offset;
                Array.Copy(buffer, buffer.Length - offset, buffer, 0, offset);
                position += bufferSize - offset;
            }
        }

        private static T[] Concat<T>(T[] a1, T[] a2)
        {
            // https://stackoverflow.com/a/50956326/1582323
            // For spans: https://github.com/dotnet/runtime/issues/30140#issuecomment-509375982

            T[] array = new T[a1.Length + a2.Length];
            Array.Copy(a1, 0, array, 0, a1.Length);
            Array.Copy(a2, 0, array, a1.Length, a2.Length);
            return array;
        }

        
        ///// <summary>
        ///// Returns NULL if there are no chunks in this buffer
        ///// returns the position of the next chunk
        ///// </summary>
        ///// <param name="buffer"></param>
        ///// <returns></returns>
        //private SequencePosition? NextChunk(ref ReadOnlySequence<byte> buffer)
        //{
        //    SequencePosition offset = buffer.Start;

        //    while (true)
        //    {
        //        var nextDelimiterPosition = buffer.Slice(offset).PositionOf(Delimiter[0]);

        //        if (nextDelimiterPosition is null || nextDelimiterPosition.Value.Equals(buffer.End))
        //        {
        //            // no delimiting chars anymore in this buffer
        //            return null;
        //        }

        //        var delimiterSlice = buffer.Slice(offset).Slice(nextDelimiterPosition.Value, Delimiter.Length); //TODO quid '0' op t einde? //TODO2 quid 0 op t einde en 0 in t begin van de volgende
        //        if (!delimiterSlice.FirstSpan.StartsWith(Delimiter))
        //        {
        //            // only the first byte of the delimiter matched, the ones after that not -- continue searching
        //            offset = buffer.GetPosition(1, nextDelimiterPosition.Value);
        //            continue;
        //        }

        //        var nextChunkPosition = buffer.GetPosition(Delimiter.Length, nextDelimiterPosition.Value); //include the delimiter in the next chunk
        //        var chunk = buffer.Slice(0, nextChunkPosition);

        //        if (chunk.Length < 1024)
        //        {
        //            // this chunk is too small
        //            offset = buffer.GetPosition(1, nextChunkPosition);
        //            continue;
        //        }
        //        else
        //        {
        //            // this is a good chunk
        //            return nextChunkPosition;
        //        }
        //    }
        //}
    }
}
