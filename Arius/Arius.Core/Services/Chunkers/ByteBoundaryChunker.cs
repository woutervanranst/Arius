using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.HighPerformance;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Services.Chunkers
{
    internal class ByteBoundaryChunker : Chunker
    {
        public ByteBoundaryChunker(ILogger<ByteBoundaryChunker> logger, TempDirectoryAppSettings config, IHashValueProvider hvp) : base(hvp)
        {
            this.logger = logger;
            uploadTempDirFullName = config.TempDirectoryFullName;

            useMemory = true;
        }

        private readonly ILogger<ByteBoundaryChunker> logger;
        private readonly string uploadTempDirFullName;
        private readonly bool useMemory;

        private const int NUMBER_CONSECUTIVE_DELIMITER = 2; // 2 bytes = 16 bits gives chunks of 64 KB
        private const int DELIMITER = 0;

        public override IChunkFile[] Chunk(BinaryFile bf)
        {
            logger.LogInformation($"Chunking '{bf.Name}'... ");

            var di = new DirectoryInfo(Path.Combine(uploadTempDirFullName, "chunks", $"{bf.Hash}"));
            if (di.Exists) di.Delete(true);
            di.Create();

            string chunkFullFileName(int i) => Path.Combine(di.FullName, $@"{bf.Name}.{i}");

            var chunks = CreateChunks(bf, chunkFullFileName).ToArray();
            var deduped = chunks.GroupBy(p => p.Hash).Where(g => g.Count() > 1).ToList();
            var netSavedBytes = deduped.Sum(g => g.First().Length * (g.Count() - 1));

            logger.LogInformation($"Chunking '{bf.Name}'... done into {chunks.Length} chunks, {deduped.Count} in-file duplicates, saving {netSavedBytes.GetBytesReadable()}");

            return chunks;
        }

        public IEnumerable<ReadOnlyMemory<byte>> Chunk(Stream stream)
        {
            int b; //the byte being read
            int c = NUMBER_CONSECUTIVE_DELIMITER;

            using var chunkMemoryStream = new MemoryStream();

            while ((b = stream.ReadByte()) != -1) //-1 = end of the stream
            {
                chunkMemoryStream.WriteByte((byte)b);

                if (b == DELIMITER)
                    c--;
                else
                    c = NUMBER_CONSECUTIVE_DELIMITER;

                if ((c <= 0 && chunkMemoryStream.Length > 1024) || //at least blocks of 1KB
                    stream.Position == stream.Length)
                {
                    var r = chunkMemoryStream.ToArray().AsMemory();

                    chunkMemoryStream.Flush();

                    yield return r;
                    
                    //.Dispose();
                    //chunkMemoryStream = new MemoryStream();
                }
            }
        }

        private IEnumerable<IChunkFile> CreateChunks(BinaryFile bf, Func<int, string> chunkFullFileName)
        {
            /* Idea for future optimization:
             *      
             *      1. With Span: https://stackoverflow.com/a/58347430/1582323
             *      
             *      using var fs = File.OpenRead(bf.FullName);
             *      using var ms = new MemoryStream();
             *      fs.CopyTo(ms);
             *      
             *      byte[] twoZeroes = { 0, 0 };
             *      
             *      var span = new ReadOnlySpan<byte>(ms.GetBuffer());
             *      var z = span.IndexOf(twoZeroes);
             *      
             *      
             *      2. With Pipes?
             *      var pipe = new Pipe();
             *      await stream.CopyToAsync(pipe.Writer);
             *      https://stackoverflow.com/questions/53801581/using-system-io-pipelines-together-with-stream
             *      https://github.com/AArnott/Nerdbank.Streams/blob/main/doc/Sequence.md ?
             */

            var chunkMemoryStream = new MemoryStream();
            Stream stream = default;

            try
            {
                if (useMemory)
                {
                    //Copy the full file in RAM for faster (x10) speed
                    stream = new MemoryStream();
                    using var fs = File.OpenRead(bf.FullName);
                    fs.CopyTo(stream);
                    stream.Position = 0;
                }
                else
                {
                    //Read the file from disk
                    stream = File.OpenRead(bf.FullName);
                }
                

                int b; //the byte being read
                int i = 0; //chunk index number
                int c = NUMBER_CONSECUTIVE_DELIMITER;

                while ((b = stream.ReadByte()) != -1) //-1 = end of the stream
                {
                    chunkMemoryStream.WriteByte((byte)b);

                    if (b == DELIMITER)
                        c--;
                    else
                        c = NUMBER_CONSECUTIVE_DELIMITER;

                    if ((c <= 0 && chunkMemoryStream.Length > 1024) || //at least blocks of 1KB
                        stream.Position == stream.Length)
                    {
                        var filename = chunkFullFileName(i);

                        // Write the chunk to file
                        using (var cfs = File.OpenWrite(filename))
                        {
                            chunkMemoryStream.WriteTo(cfs);
                        }

                        //Calculate the hash
                        chunkMemoryStream.Position = 0;
                        var hash = hvp.GetChunkHash(chunkMemoryStream);

                        logger.LogDebug($"Chunking '{bf.Name}'... wrote chunk {i}, progress: {Math.Round(stream.Position / (double)stream.Length * 100)}%");

                        //Reset for next iteration
                        i++;
                        c = NUMBER_CONSECUTIVE_DELIMITER;
                        chunkMemoryStream = new MemoryStream();

                        yield return new ChunkFile(new FileInfo(filename), hash);
                    }
                }
            }
            finally
            {
                chunkMemoryStream.Dispose();
                stream?.Dispose();
            }
        }


        //private IEnumerable<IChunkFile> CreateChunks_BUFFERED(string sourceFileName, Func<int, string> chunkFullFileName)
        //{
        //    // NOTE: This implementation is WRONG and not faster than the one above

        //    using var stream = File.OpenRead(sourceFileName);
        //    var chunkMemoryStream = new MemoryStream();

        //    try
        //    {
        //        const int bufferSize = 64 * 1024;
        //        var buffer = new byte[bufferSize];
        //        var pos = 0L;
        //        var last = 0L;

        //        const int NUMBER_CONSECUTIVE_DELIMITER = 2; // 2 bytes = 16 bits gives chunks of 64 KB
        //        const int DELIMITER = 0;

        //        var c = NUMBER_CONSECUTIVE_DELIMITER;

        //        int chunkCounter = 0; //chunk index number


        //        while (true)
        //        {
        //            var bytesRead = stream.Read(buffer, 0, (int)Math.Min(bufferSize, stream.Length - pos));
        //            for (int i = 0; i < bytesRead; i++)
        //            {
        //                pos++;

        //                if (buffer[i] == DELIMITER)
        //                    c--;
        //                else
        //                    c = NUMBER_CONSECUTIVE_DELIMITER;

        //                if ((c <= 0 && pos - last > 1024) //at least blocks of 1KB
        //                    || pos == stream.Length)    // EOF
        //                {
        //                    //write the chunk to the buffer
        //                    chunkMemoryStream.Write(buffer, 0, (int)(pos - last));

        //                    //--> return the results to the caller

        //                    yield return writechunk(chunkMemoryStream);

        //                    //Reset for next iteration
        //                    last = pos;
        //                    chunkCounter++;
        //                    c = NUMBER_CONSECUTIVE_DELIMITER;
        //                    chunkMemoryStream = new MemoryStream();
        //                }
        //            }
        //            if (bytesRead == 0) 
        //                break;
                    
        //            if (last != pos)
        //            {
        //                chunkMemoryStream.Write(buffer, 0, (int)(pos - last));
        //                last = pos;
        //            }
        //        }


        //        ChunkFile writechunk(Stream s)
        //        {
        //            var filename = chunkFullFileName(chunkCounter);

        //            // Write the chunk to file
        //            using (var cfs = File.OpenWrite(filename))
        //            {
        //                chunkMemoryStream.WriteTo(cfs);
        //            }

        //            //Calculate the hash
        //            chunkMemoryStream.Position = 0;
        //            var hash = hvp.GetHashValue(chunkMemoryStream);

                    

        //            return new ChunkFile(new FileInfo(filename), hash);

        //        };
        //    }
        //    finally
        //    {
        //        chunkMemoryStream.Dispose();
        //    }
        //}
    }
}
