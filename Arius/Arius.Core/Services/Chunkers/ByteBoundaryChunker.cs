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
    internal class ByteBoundaryChunker
    {
        public ByteBoundaryChunker(ILogger<ByteBoundaryChunker> logger, int bufferSize = 8192, int minChunkSize = 1024) // : base(hvp)
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






        public IEnumerable<byte[]> Chunk(Stream stream)
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
                    var chunk = buffer[offset..(i + Delimiter.Length)]; //ro.Slice(0, i + delimiter.Length).ToArray();
                    yield return Concat(nextChunk, chunk);

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
                    //var chunk = buffer[offset..(bytesRead + Delimiter.Length)]; //return the bytes read
                    var chunk = buffer[offset..(bytesRead + offset)]; //return the bytes read
                    yield return Concat(nextChunk, chunk);

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










        

        public async IAsyncEnumerable<byte[]> ChunkAsync(Stream stream)
        {
            var pipe = new Pipe(new PipeOptions(/*minimumSegmentSize: 120000*/));
            var writing = FillPipeAsync(stream, pipe.Writer).ConfigureAwait(false);

            await foreach (var chunk in ReadPipeAsync2(pipe.Reader))
            {
                yield return chunk;
            };

            await writing;
        }

        private static async Task FillPipeAsync(Stream stream, PipeWriter writer, CancellationToken cancellationToken = default)
        {
            const int bufferSize = 4096;

            while (true)
            {
                Memory<byte> memory = writer.GetMemory(bufferSize);
                int bytesRead = await stream.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) 
                    break;

                writer.Advance(bytesRead);

                var result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                
                if (result.IsCompleted) 
                    break;
            }

            await writer.CompleteAsync().ConfigureAwait(false);
        }

        //private async IAsyncEnumerable<byte[]> ReadPipeAsync(PipeReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        //{
        //    var nextChunk = new byte[0];

        //    while (true)
        //    {
        //        var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        //        var buffer = result.Buffer;
        //        var bs = buffer.Start;
        //        var be = buffer.End;

        //        while (TryReadChunk(ref buffer, delimiter.Span, out var isChunk))
        //        {
        //            nextChunk = Concat(nextChunk, chunk);
        //            if (isChunk)
        //            { 
        //                yield return nextChunk;
        //                nextChunk = new byte[0];
        //            }

        //        }

        //        reader.AdvanceTo(bs, be); //buffer is null after TryReadChunk returns false so buffer.Start and buffer.End need to be cached
        //        if (result.IsCompleted)
        //        {
        //            yield return nextChunk; // last chunk in the buffer
        //            break;
        //        }
        //    }

        //    await reader.CompleteAsync().ConfigureAwait(false);
        //}

        private async IAsyncEnumerable<byte[]> ReadPipeAsync2(PipeReader reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var nextChunk = new byte[0];

            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;

                var nextChunkPosition = NextChunk(ref buffer);
                
                if (nextChunkPosition is null)
                {
                    nextChunk = Concat(nextChunk, buffer.ToArray());
                    nextChunkPosition = buffer.End; // set to advance to end of the buffer
                }
                else
                {
                    nextChunk = Concat(nextChunk, buffer.Slice(0, nextChunkPosition.Value).ToArray());
                    yield return nextChunk;

                    nextChunk = new byte[0];
                }

                reader.AdvanceTo(nextChunkPosition.Value);
                if (result.IsCompleted)
                {
                    yield return nextChunk; // last chunk in the stream
                    break;
                }
            }
        }

        public static T[] Concat<T>(T[] a1, T[] a2)
        {
            // https://stackoverflow.com/a/50956326/1582323
            // For spans: https://github.com/dotnet/runtime/issues/30140#issuecomment-509375982

            T[] array = new T[a1.Length + a2.Length];
            Array.Copy(a1, 0, array, 0, a1.Length);
            Array.Copy(a2, 0, array, a1.Length, a2.Length);
            return array;
        }

        //public static ReadOnlySequence<byte> Concat(ReadOnlySequence<byte> s1, ReadOnlySequence<byte> s2)
        //{
        //    new ReadOnlySequence(s1.f
        //        )
        //    var ss1 = new Segment(new ReadOnlyMemory s1.as);


        //    return new ReadOnlySequence<byte>(new ReadOnlySequenceSegment<byte>() s1, 0, s2, 1);
        //    var array = new byte[s1.Length + s2.Length];
        //    s1.CopyTo(array);
        //    s2.CopyTo(array.AsSpan(s1.Length));
        //    return array;
        //}

        //class Segment<T> : ReadOnlySequenceSegment<T>
        //{
        //    public Segment(ReadOnlyMemory<T> memory) => Memory = memory;

        //    public Segment<T> Add(ReadOnlyMemory<T> mem)
        //    {
        //        var segment = new Segment<T>(mem);
        //        segment.RunningIndex = RunningIndex + Memory.Length;

        //        Next = segment;
        //        return segment;
        //    }
        //}




        
        /// <summary>
        /// Returns NULL if there are no chunks in this buffer
        /// returns the position of the next chunk
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private SequencePosition? NextChunk(ref ReadOnlySequence<byte> buffer)
        {
            SequencePosition offset = buffer.Start;

            while (true)
            {
                var nextDelimiterPosition = buffer.Slice(offset).PositionOf(Delimiter[0]);

                if (nextDelimiterPosition is null || nextDelimiterPosition.Value.Equals(buffer.End))
                {
                    // no delimiting chars anymore in this buffer
                    return null;
                }

                var delimiterSlice = buffer.Slice(offset).Slice(nextDelimiterPosition.Value, Delimiter.Length); //TODO quid '0' op t einde? //TODO2 quid 0 op t einde en 0 in t begin van de volgende
                if (!delimiterSlice.FirstSpan.StartsWith(Delimiter))
                {
                    // only the first byte of the delimiter matched, the ones after that not -- continue searching
                    offset = buffer.GetPosition(1, nextDelimiterPosition.Value);
                    continue;
                }

                var nextChunkPosition = buffer.GetPosition(Delimiter.Length, nextDelimiterPosition.Value); //include the delimiter in the next chunk
                var chunk = buffer.Slice(0, nextChunkPosition);

                if (chunk.Length < 1024)
                {
                    // this chunk is too small
                    offset = buffer.GetPosition(1, nextChunkPosition);
                    continue;
                }
                else
                {
                    // this is a good chunk
                    return nextChunkPosition;
                }
            }

        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="buffer"></param>
        ///// <param name="delimiter"></param>
        ///// <param name="chunk"></param>
        ///// <param name="isChunk"></param>
        ///// <returns>True if we still need to read the buffer, false if the buffer is empty</returns>
        //private bool TryReadChunk(ref ReadOnlySequence<byte> buffer, out byte[] chunk, out bool isChunk)
        //{
        //    SequencePosition offset = buffer.Start;

        //    while (true)
        //    {
        //        if (buffer.IsEmpty)
        //        {
        //            // we re at the end of the buffer / the buffer is empty
        //            chunk = default;
        //            isChunk = false;
        //            return false;
        //        }

        //        var nextDelimiterPosition = buffer.Slice(offset).PositionOf(delimiter.Span[0]);

        //        if (nextDelimiterPosition is null || nextDelimiterPosition.Value.Equals(buffer.End))
        //        {
        //            // there are no more delimiting characters in the remaining buffer - this is the last chunk in the buffer
        //            chunk = buffer.ToArray();
        //            isChunk = false; //this is no chunk unless it is the last one in the STREAM - which we re not reading here
        //            buffer = default;
        //            return true;
        //        }

        //        var delimiterSlice = buffer.Slice(offset).Slice(nextDelimiterPosition.Value, delimiter.Length);
        //        if (!delimiterSlice.FirstSpan.StartsWith(delimiter))
        //        {
        //            offset = buffer.GetPosition(1, nextDelimiterPosition.Value);
        //            continue;
        //        }

                

        //        var nextChunkPosition = buffer.GetPosition(delimiter.Length, nextDelimiterPosition.Value);
        //        var c = buffer.Slice(0, nextChunkPosition);

        //        if (c.Length < 1024)
        //        {
        //            // this chunk is too small
        //            offset = buffer.GetPosition(1, nextChunkPosition);
        //            continue;
        //        }
        //        else
        //        {
        //            // this is a valid chunk
        //            chunk = c.ToArray();
        //            buffer = buffer.Slice(nextChunkPosition);
        //            isChunk = true;
        //            return true;
        //        }
        //    }
        //}

        



        //private readonly string uploadTempDirFullName;
        //private readonly bool useMemory;

        //private const int NUMBER_CONSECUTIVE_DELIMITER = 2; // 2 bytes = 16 bits gives chunks of 64 KB
        //private const int DELIMITER = 0;

        //public override IChunkFile[] Chunk(BinaryFile bf)
        //{
        //    logger.LogInformation($"Chunking '{bf.Name}'... ");

        //    var di = new DirectoryInfo(Path.Combine(uploadTempDirFullName, "chunks", $"{bf.Hash}"));
        //    if (di.Exists) di.Delete(true);
        //    di.Create();

        //    string chunkFullFileName(int i) => Path.Combine(di.FullName, $@"{bf.Name}.{i}");

        //    var chunks = CreateChunks(bf, chunkFullFileName).ToArray();
        //    var deduped = chunks.GroupBy(p => p.Hash).Where(g => g.Count() > 1).ToList();
        //    var netSavedBytes = deduped.Sum(g => g.First().Length * (g.Count() - 1));

        //    logger.LogInformation($"Chunking '{bf.Name}'... done into {chunks.Length} chunks, {deduped.Count} in-file duplicates, saving {netSavedBytes.GetBytesReadable()}");

        //    return chunks;
        //}

        //public IEnumerable<ReadOnlyMemory<byte>> Chunk(Stream stream)
        //{
        //    var chunk = new MemoryStream();

        //    try
        //    {
        //        int b; //the byte being read
        //        int c = NUMBER_CONSECUTIVE_DELIMITER;

        //        while ((b = stream.ReadByte()) != -1) //-1 = end of the stream
        //        {
        //            chunk.WriteByte((byte)b);

        //            if (b == DELIMITER)
        //                c--;
        //            else
        //                c = NUMBER_CONSECUTIVE_DELIMITER;

        //            if ((c <= 0 && chunk.Length > 1024) || //at least blocks of 1KB
        //                stream.Position == stream.Length)
        //            {
        //                var r = chunk.ToArray().AsMemory();

        //                chunk.Dispose();
        //                chunk = new();

        //                yield return r;
        //            }
        //        }
        //    }
        //    finally
        //    {
        //        chunk.Dispose();
        //    }
        //}

        //private IEnumerable<IChunkFile> CreateChunks(BinaryFile bf, Func<int, string> chunkFullFileName)
        //{
        //    /* Idea for future optimization:
        //     *      
        //     *      1. With Span: https://stackoverflow.com/a/58347430/1582323
        //     *      
        //     *      using var fs = File.OpenRead(bf.FullName);
        //     *      using var ms = new MemoryStream();
        //     *      fs.CopyTo(ms);
        //     *      
        //     *      byte[] twoZeroes = { 0, 0 };
        //     *      
        //     *      var span = new ReadOnlySpan<byte>(ms.GetBuffer());
        //     *      var z = span.IndexOf(twoZeroes);
        //     *      
        //     *      
        //     *      2. With Pipes?
        //     *      var pipe = new Pipe();
        //     *      await stream.CopyToAsync(pipe.Writer);
        //     *      https://stackoverflow.com/questions/53801581/using-system-io-pipelines-together-with-stream
        //     *      https://github.com/AArnott/Nerdbank.Streams/blob/main/doc/Sequence.md ?
        //     */

        //    var chunkMemoryStream = new MemoryStream();
        //    Stream stream = default;

        //    try
        //    {
        //        if (useMemory)
        //        {
        //            //Copy the full file in RAM for faster (x10) speed
        //            stream = new MemoryStream();
        //            using var fs = File.OpenRead(bf.FullName);
        //            fs.CopyTo(stream);
        //            stream.Position = 0;
        //        }
        //        else
        //        {
        //            //Read the file from disk
        //            stream = File.OpenRead(bf.FullName);
        //        }
                

        //        int b; //the byte being read
        //        int i = 0; //chunk index number
        //        int c = NUMBER_CONSECUTIVE_DELIMITER;

        //        while ((b = stream.ReadByte()) != -1) //-1 = end of the stream
        //        {
        //            chunkMemoryStream.WriteByte((byte)b);

        //            if (b == DELIMITER)
        //                c--;
        //            else
        //                c = NUMBER_CONSECUTIVE_DELIMITER;

        //            if ((c <= 0 && chunkMemoryStream.Length > 1024) || //at least blocks of 1KB
        //                stream.Position == stream.Length)
        //            {
        //                var filename = chunkFullFileName(i);

        //                // Write the chunk to file
        //                using (var cfs = File.OpenWrite(filename))
        //                {
        //                    chunkMemoryStream.WriteTo(cfs);
        //                }

        //                //Calculate the hash
        //                chunkMemoryStream.Position = 0;
        //                var hash = hvp.GetChunkHash(chunkMemoryStream);

        //                logger.LogDebug($"Chunking '{bf.Name}'... wrote chunk {i}, progress: {Math.Round(stream.Position / (double)stream.Length * 100)}%");

        //                //Reset for next iteration
        //                i++;
        //                c = NUMBER_CONSECUTIVE_DELIMITER;
        //                chunkMemoryStream.Dispose();
        //                chunkMemoryStream = new();

        //                yield return new ChunkFile(new FileInfo(filename), hash);
        //            }
        //        }
        //    }
        //    finally
        //    {
        //        chunkMemoryStream.Dispose();
        //        stream?.Dispose();
        //    }
        //}
        
    }
}
