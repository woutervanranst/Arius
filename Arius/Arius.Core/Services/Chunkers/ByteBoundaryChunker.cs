﻿using Arius.Core.Configuration;
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
        public ByteBoundaryChunker(ILogger<ByteBoundaryChunker> logger) // : base(hvp)
        {
            this.logger = logger;
        }

        private readonly ILogger<ByteBoundaryChunker> logger;

        // ReadOnlySequence<T>.AsStream() -- see https://github.com/AArnott/Nerdbank.Streams/blob/main/doc/AsStream.md#readonlysequencebyte, may come native in .NET 6 https://github.com/dotnet/runtime/issues/27156

        // Good intro on Buffers: https://docs.microsoft.com/en-us/dotnet/standard/io/buffers#readonlysequencet

        public async IAsyncEnumerable<byte[]> ChunkAsync(Stream stream)
        {
            var pipe = new Pipe(new PipeOptions(/*minimumSegmentSize: 120000*/));
            var writing = FillPipeAsync(stream, pipe.Writer).ConfigureAwait(false);
            var delimiter = new ReadOnlyMemory<byte>(new byte[] { 0, 0 });

            await foreach (var chunk in ReadPipeAsync(pipe.Reader, delimiter))
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

        private static async IAsyncEnumerable<byte[]> ReadPipeAsync(PipeReader reader, ReadOnlyMemory<byte> delimiter, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var nextChunk = new byte[0];

            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;
                var bs = buffer.Start;
                var be = buffer.End;

                while (TryReadChunk(ref buffer, delimiter.Span, out var chunk, out var isChunk))
                {
                    nextChunk = Concat(nextChunk, chunk);
                    if (isChunk)
                    { 
                        yield return nextChunk;
                        nextChunk = new byte[0];
                    }

                }

                reader.AdvanceTo(bs, be); //buffer is null after TryReadChunk returns false so buffer.Start and buffer.End need to be cached
                if (result.IsCompleted)
                {
                    yield return nextChunk; // last chunk in the buffer
                    break;
                }
            }

            await reader.CompleteAsync().ConfigureAwait(false);
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
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="delimiter"></param>
        /// <param name="chunk"></param>
        /// <param name="isChunk"></param>
        /// <returns>True if we still need to read the buffer, false if the buffer is empty</returns>
        private static bool TryReadChunk(ref ReadOnlySequence<byte> buffer, ReadOnlySpan<byte> delimiter, out byte[] chunk, out bool isChunk)
        {
            SequencePosition offset = buffer.Start;

            while (true)
            {
                if (buffer.IsEmpty)
                {
                    // we re at the end of the buffer / the buffer is empty
                    chunk = default;
                    isChunk = false;
                    return false;
                }

                var nextDelimiterPosition = buffer.Slice(offset).PositionOf(delimiter[0]);

                if (nextDelimiterPosition is null || nextDelimiterPosition.Value.Equals(buffer.End))
                {
                    // there are no more delimiting characters in the remaining buffer - this is the last chunk in the buffer
                    chunk = buffer.ToArray();
                    isChunk = false; //this is no chunk unless it is the last one in the STREAM - which we re not reading here
                    buffer = default;
                    return true;
                }

                var delimiterSlice = buffer.Slice(offset).Slice(nextDelimiterPosition.Value, delimiter.Length);
                if (!delimiterSlice.FirstSpan.StartsWith(delimiter))
                {
                    offset = buffer.GetPosition(1, nextDelimiterPosition.Value);
                    continue;
                }

                

                var nextChunkposition = buffer.GetPosition(delimiter.Length, nextDelimiterPosition.Value);
                var c = buffer.Slice(0, nextChunkposition);

                if (c.Length < 1024)
                {
                    // this chunk is too small
                    offset = buffer.GetPosition(1, nextChunkposition);
                    continue;
                }
                else
                {
                    // this is a valid chunk
                    chunk = c.ToArray();
                    buffer = buffer.Slice(nextChunkposition);
                    isChunk = true;
                    return true;
                }
            }
        }

        



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
