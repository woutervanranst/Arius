﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Services.Chunkers
{
    internal class RabinKarpChunker : Chunker
    {
        public RabinKarpChunker(ILogger<RabinKarpChunker> logger, TempDirectoryAppSettings config, IHashValueProvider hvp) : base(hvp)
        {
            _logger = logger;

            _uploadTempDirFullName = config.TempDirectoryFullName;
        }

        private static readonly StreamBreaker _sb = new();
        private readonly ILogger<RabinKarpChunker> _logger;
        private readonly string _uploadTempDirFullName;

        public override IChunkFile[] Chunk(BinaryFile bf)
        {
            _logger.LogInformation($"Chunking {bf.RelativeName}...");

            var di = new DirectoryInfo(Path.Combine(_uploadTempDirFullName, "chunks", $"{bf.RelativeName}"));
            if (di.Exists)
                di.Delete(true);
            di.Create();

            using var bffs = new FileStream(bf.FullName, FileMode.Open, FileAccess.Read);
            var chunkDefs = _sb.GetChunks(bffs, bffs.Length, SHA256.Create()).ToArray();
            bffs.Position = 0;

            var chunks = new List<ChunkFile>();

            for (int i = 0; i < chunkDefs.Count(); i++)
            {
                _logger.LogInformation($"Writing chunk {i}/{chunkDefs.Length} of {bf.RelativeName}");
                var chunk = chunkDefs[i];

                byte[] buff = new byte[chunk.Length];
                bffs.Read(buff, 0, (int)chunk.Length);

                //TODO KARL if exception thrown here it 'vanishes'

                var chunkFullName = Path.Combine(di.FullName, $@"{bf.Name}.{i.ToString().PadLeft(chunkDefs.Count().ToString().Length, '0')}");
                using var fileStream = File.Create(chunkFullName);
                fileStream.Write(buff, 0, (int)chunk.Length);
                fileStream.Close();

                var hashValue = hvp.GetChunkHash(chunkFullName);
                chunks.Add(new ChunkFile(new FileInfo(chunkFullName), hashValue));

                //var di = new DirectoryInfo(Path.Combine(_uploadTempDir.FullName, "chunks", $"{bf.Name}.arius"));
                //if (di.Exists)
                //    di.Delete();
                //di.Create();

                //using var bffs = new FileStream(bf.FullName, FileMode.Open, FileAccess.Read);
                //var chunkDefs = _sb.GetChunks(bffs, bffs.Length, SHA256.Create()).ToArray();
                //bffs.Position = 0;

                //var chunks = new List<ChunkFile>();

                //for (int i = 0; i < chunkDefs.Count(); i++)
                //{
                //    var chunk = chunkDefs[i];

                //    byte[] buff = new byte[chunk.Length];
                //    bffs.Read(buff, 0, (int)chunk.Length);

                //    var chunkFullName = $@"{di.FullName}\{i}";
                //    using var fileStream = File.Create(chunkFullName);
                //    fileStream.Write(buff, 0, (int)chunk.Length);
                //    fileStream.Close();

                //    var hashValue = _hvp.GetHashValue(chunkFullName);
                //    chunks.Add(new ChunkFile(bf.Root, new FileInfo($@"{di.FullName}\{i}"), hashValue));
            }

            _logger.LogInformation($"Chunking {bf.RelativeName}... done");

            return chunks.ToArray();
        }

        /// <summary>Uses a rolling hash (Rabin-Karp) to segment a large file</summary>
        // https://www.codeproject.com/Articles/801608/Using-a-rolling-hash-to-break-up-binary-files
        /// <remarks>
        /// For a given window w of size n, and a prime number p the RK hash is computed:
        /// p^n(w[n]) + p^n-1(w[n-1]) + ... + p^0(w[0])
        /// 
        /// This hash is such that a contributing underlying value 
        /// can be removed from the big end and a new value added to the small end.
        /// So, a circular queue keeps track of the contributing values.
        /// 
        /// Hash of each chunk is also computed progressively using a 
        /// stronger hash algorithm of the caller's choice.
        /// </remarks>
        private class StreamBreaker
        {
            private static int width = 64;  //--> the # of bytes in the window
            private const long seed = 2273;  //--> a our hash seed
            private static long mask = (1 << 16) - 1;  //--> a hash seive: 16 gets you ~64k chunks
            private const int bufferSize = 64 * 1024;

            public static void SetWindowSizeAndMask(int width, long mask)
            {
                StreamBreaker.width = width;
                StreamBreaker.mask = mask;
            }

            /// <summary>
            /// Subdivides a stream using a Rabin-Karp rolling hash to find sentinal locations
            /// </summary>
            /// <param name="stream">The stream to read</param>
            /// <param name="length">The length to read</param>
            /// <param name="hasher">A hash algorithm to create a strong hash over the segment</param>
            /// <remarks>
            /// We may be reading a stream out of a BackupRead operation.  
            /// In this case, the stream <b>might not</b> be positioned at 0 
            /// and may be longer than the section we want to read.
            /// So...we keep track of length and don't arbitrarily read 'til we get zero
            /// 
            /// Also, overflows occur when getting maxSeed and when calculating the hash.
            /// These overflows are expected and not significant to the computation.
            /// </remarks>
            public IEnumerable<Chunk> GetChunks(Stream stream, long length, HashAlgorithm hasher)
            {
                var maxSeed = seed; //--> will be prime^width after initialization (sorta)
                var buffer = new byte[bufferSize];
                var circle = new byte[width];  //--> the circular queue: input to the hash functions
                var hash = 0L;  //--> our rolling hash
                var circleIndex = 0;  //--> index into circular queue
                var last = 0L;  //--> last place we started a new segment
                var pos = 0L;  //--> the position we're at in the range of stream we're reading

                //--> initialize maxSeed...
                for (int i = 0; i < width; i++) maxSeed *= maxSeed;

                while (true)
                {
                    //--> Get some bytes to work on (don't let it read past length)
                    var bytesRead = stream.Read(buffer, 0, (int)Math.Min(bufferSize, length - pos));
                    for (int i = 0; i < bytesRead; i++)
                    {
                        pos++;
                        hash = buffer[i] + (hash - maxSeed * circle[circleIndex]) * seed;
                        circle[circleIndex++] = buffer[i];
                        if (circleIndex == width) circleIndex = 0;
                        if ((hash | mask) == hash || pos == length)  //--> match or EOF
                        {
                            //--> apply the strong hash to the remainder of the bytes in the circular queue...
                            hasher.TransformFinalBlock(circle, 0, circleIndex == 0 ? width : circleIndex);

                            //--> return the results to the caller...
                            yield return new Chunk(last, pos - last, hasher.Hash);
                            last = pos;

                            //--> reset the hashes...
                            hash = 0;
                            for (int j = 0; j < width; j++) circle[j] = 0;
                            circleIndex = 0;
                            hasher.Initialize();
                        }
                        else
                        {
                            if (circleIndex == 0) hasher.TransformBlock(circle, 0, width, circle, 0);
                        }
                    }
                    if (bytesRead == 0) break;
                }
            }

            /// <summary>A description of a chunk</summary>
            public struct Chunk
            {
                /// <summary>How far into the strem we found the chunk</summary>
                public readonly long Offset;
                /// <summary>How long the chunk is</summary>
                public readonly long Length;
                /// <summary>Strong hash for the chunk</summary>
                public readonly byte[] Hash;

                internal Chunk(long offset, long length, byte[] hash)
                {
                    Offset = offset;
                    Length = length;
                    Hash = hash;
                }
            }
        }
    }

    
}