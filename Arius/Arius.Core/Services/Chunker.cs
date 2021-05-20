using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Arius.Core.Commands;
using Arius.Core.Extensions;
using Arius.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Services
{
    internal interface IChunker
    {
        IChunkFile[] Chunk(BinaryFile fileToChunk);
        BinaryFile Merge(IChunkFile[] chunksToJoin, FileInfo target);
    }

    
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
        public DedupChunker(ILogger<DedupChunker> logger, ITempDirectoryAppSettings config, IHashValueProvider hvp)
        {
            _logger = logger;
            _hvp = hvp;

            _uploadTempDirFullName = config.TempDirectoryFullName;

        }

        private static readonly StreamBreaker _sb = new();
        private readonly ILogger<DedupChunker> _logger;
        private readonly IHashValueProvider _hvp;
        private readonly string _uploadTempDirFullName;

        public IChunkFile[] Chunk(BinaryFile bf)
        {
            _logger.LogInformation($"Chunking {bf.RelativeName}...");

            var di = new DirectoryInfo(Path.Combine(_uploadTempDirFullName, "chunks", $"{bf.RelativeName}"));
            if (di.Exists)
                di.Delete();
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

                var hashValue = _hvp.GetHashValue(chunkFullName);
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
