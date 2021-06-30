using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;
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
        public ByteBoundaryChunker(ILogger<ByteBoundaryChunker> logger, TempDirectoryAppSettings config, IHashValueProvider hvp)
        {
            this.logger = logger;
            this.hvp = hvp;
            uploadTempDirFullName = config.TempDirectoryFullName;
        }

        private readonly ILogger<ByteBoundaryChunker> logger;
        private readonly IHashValueProvider hvp;
        private readonly string uploadTempDirFullName;

        public override IChunkFile[] Chunk(BinaryFile bf)
        {
            var di = new DirectoryInfo(Path.Combine(uploadTempDirFullName, "chunks", $"{bf.RelativeName}"));
            if (di.Exists)
                di.Delete(true);
            di.Create();

            string chunkFullFileName(int i) => Path.Combine(di.FullName, $@"{bf.Name}.{i}");

            var chunks = CreateChunks(bf.FullName, chunkFullFileName).ToArray();
            var deduped = chunks.GroupBy(p => p.Hash).Where(g => g.Count() > 1).ToList();
            var netSavedBytes = deduped.Sum(g => g.First().Length * (g.Count() - 1));

            logger.LogInformation($"Chunked {bf.Name} into {chunks.Length} chunks, {deduped.Count} in-file duplicates, saving {netSavedBytes.GetBytesReadable()}");

            return chunks;
        }

        private IEnumerable<IChunkFile> CreateChunks(string sourceFileName, Func<int, string> chunkFullFileName)
        {
            var chunkMemoryStream = new MemoryStream();

            try
            {
                const int NUMBER_CONSECUTIVE_DELIMITER = 2; // 2 bytes = 16 bits gives chunks of 64 KB
                const int DELIMITER = 0;

                using var fs = File.OpenRead(sourceFileName);

                int b; //the byte being read
                int i = 0; //chunk index number
                int c = NUMBER_CONSECUTIVE_DELIMITER;


                while ((b = fs.ReadByte()) != -1) //-1 = end of the stream
                {
                    chunkMemoryStream.WriteByte((byte)b);

                    if (b == DELIMITER)
                        c--;
                    else
                        c = NUMBER_CONSECUTIVE_DELIMITER;

                    if ((c <= 0 && chunkMemoryStream.Length > 1024) || //at least blocks of 1KB
                        fs.Position == fs.Length)
                    {
                        var filename = chunkFullFileName(i);

                        // Write the chunk to file
                        using (var cfs = File.OpenWrite(filename))
                        {
                            chunkMemoryStream.WriteTo(cfs);
                        }

                        //Calculate the hash
                        chunkMemoryStream.Position = 0;
                        var hash = hvp.GetHashValue(chunkMemoryStream);

                        //Reset for next iteration
                        i++;
                        c = NUMBER_CONSECUTIVE_DELIMITER;
                        chunkMemoryStream = new MemoryStream();
                        //.SetLength(0);
                        //chunkMemoryStream.Position = 0;

                        yield return new ChunkFile(new FileInfo(filename), hash);
                    }
                }
            }
            finally
            {
                chunkMemoryStream.Dispose();
            }
            
        }
    }
}
