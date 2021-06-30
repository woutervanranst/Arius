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
                di.Delete();
            di.Create();

            string chunkFullFileName(int i) => Path.Combine(di.FullName, $@"{bf.Name}.{i}");

            var chunkFileNames = CreateChunks(bf.FullName, chunkFullFileName);

            var chunkFileNamesWithHash = chunkFileNames.Select(chunkFileName =>
            {
                var hash = hvp.GetHashValue(chunkFileName);

                return new ChunkFile(new FileInfo(chunkFileName), hash);
            }).ToArray();

            var deduped = chunkFileNamesWithHash.GroupBy(p => p.Hash).Where(g => g.Count() > 1).ToList();
            var netSavedBytes = deduped.Sum(g => g.First().Length * (g.Count() - 1));

            logger.LogInformation($"Chunked {bf.Name} into {chunkFileNamesWithHash.Length} chunks, {deduped.Count} in-file duplicates, saving {netSavedBytes.GetBytesReadable()}");

            return chunkFileNamesWithHash;
        }

        private async IEnumerable<(string, HashValue)> CreateChunks(string sourceFileName, Func<int, string> chunkFullFileName)
        {
            const int NUMBER_CONSECUTIVE_DELIMITER = 2; // 2 bytes = 16 bits gives chunks of 64 KB
            const int DELIMITER = 0;

            using var fs = File.OpenRead(sourceFileName);
            using var ms = new MemoryStream();

            int b; //the byte being read
            int i = 0; //chunk index number
            int c = NUMBER_CONSECUTIVE_DELIMITER;


            while ((b = fs.ReadByte()) != -1) //-1 = end of the stream
            {
                ms.WriteByte((byte)b);

                if (b == DELIMITER)
                    c--;
                else
                    c = NUMBER_CONSECUTIVE_DELIMITER;

                if ((c <= 0 && ms.Length > 1024) || //at least blocks of 1KB
                    fs.Position == fs.Length) 
                {
                    var fn = chunkFullFileName(i);
                    using (var cfs = File.OpenWrite(fn))
                    {
                        i++;

                        var hash = hvp.GetHashValue(ms);
                        ms.WriteTo(cfs);

                        //var hash = Task.Run(() => hvp.GetHashValue(ms));
                        //var write = Task.Run(() => ms.WriteTo(cfs));

                        //await Task.WhenAll(hash, write);

                        ms.SetLength(0);
                        ms.Position = 0;

                        c = NUMBER_CONSECUTIVE_DELIMITER;
                    }
                    yield return fn;
                }
            }
        }
    }
}
