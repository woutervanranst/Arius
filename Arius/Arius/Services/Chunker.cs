using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;

namespace Arius.Services
{
    internal interface IChunkerOptions : ICommandExecutorOptions
    {
        bool Dedup { get; }
    }
    internal class Chunker : IChunker
    {
        public IChunkFile[] Chunk(BinaryFile item)
        {
            return new[] {item};
        }

        public BinaryFile Merge(IChunkFile[] chunksToJoin, FileInfo target)
        {
            File.Move(chunksToJoin.Single().FullName, target.FullName);

            return new BinaryFile(null, target);
        }
    }


    internal class DedupChunker : IChunker
    {
        public DedupChunker(IConfiguration config, IHashValueProvider hvp)
        {
            _uploadTempDir = config.UploadTempDir;
            _hvp = hvp;
        }

        private static readonly StreamBreaker _sb = new();
        private readonly IHashValueProvider _hvp;
        private readonly DirectoryInfo _uploadTempDir;

        public IChunkFile[] Chunk(BinaryFile bf)
        {
            var di = new DirectoryInfo(Path.Combine(_uploadTempDir.FullName, "chunks", $"{bf.Name}.arius"));
            if (di.Exists)
                di.Delete();
            di.Create();

            using var bffs = new FileStream(bf.FullName, FileMode.Open, FileAccess.Read);
            var chunkDefs = _sb.GetChunks(bffs, bffs.Length, SHA256.Create()).ToArray();
            bffs.Position = 0;

            var chunks = new List<ChunkFile>();

            for (int i = 0; i < chunkDefs.Count(); i++)
            {
                var chunk = chunkDefs[i];

                byte[] buff = new byte[chunk.Length];
                bffs.Read(buff, 0, (int)chunk.Length);

                var chunkFullName = $@"{di.FullName}\{i}";
                using var fileStream = File.Create(chunkFullName);
                fileStream.Write(buff, 0, (int)chunk.Length);
                fileStream.Close();

                var hashValue = _hvp.GetHashValue(chunkFullName);
                chunks.Add(new ChunkFile(bf.Root, new FileInfo($@"{di.FullName}\{i}"), hashValue));
            }

            return chunks.ToArray();
        }

        public BinaryFile Merge(IChunkFile[] chunksToJoin, FileInfo target)
        {
            var chunkStreams = new List<Stream>();
            for (int i = 0; i < chunksToJoin.Length; i++)
                chunkStreams.Add(new FileStream(chunksToJoin[i].FullName, FileMode.Open, FileAccess.Read));

            var stream = new ConcatenatedStream(chunkStreams);

            using var targetStream = File.Create(target.FullName); // target.Create();
            stream.CopyTo(targetStream);
            targetStream.Close();

            return new BinaryFile(null, target);
        }
    }
}
