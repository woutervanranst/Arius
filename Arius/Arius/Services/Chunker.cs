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
        public IEnumerable<IChunkFile> Chunk(BinaryFile item)
        {
            return new[] {item};
        }

        public BinaryFile Merge(IEnumerable<IChunkFile> chunksToJoin, FileInfo target)
        {
            File.Move(chunksToJoin.Single().FullName, target.FullName);

            return new BinaryFile(null, target);
        }
    }


    internal class DedupChunker : IChunker
    {
        private readonly IConfiguration _config;

        public DedupChunker(IConfiguration config)
        {
            _config = config;
        }

        private static StreamBreaker _sb = new();

        public IEnumerable<IChunkFile> Chunk(BinaryFile f)
        {
            using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read);
            fs.Position = 0;
            
            var tempDir = new DirectoryInfo(Path.Combine(_config.UploadTempDir.FullName, "chunks", f.Name + ".arius"));
            tempDir.Create();

            foreach (var chunk in _sb.GetChunks(fs, fs.Length, SHA256.Create()))
            {
                var hashValue = new HashValue {Value = SHA256Hasher.ByteArrayToString(chunk.Hash)};
                
                var chunkFullName = Path.Combine(tempDir.FullName, hashValue.Value + ChunkFile.Extension);

                byte[] buff = new byte[chunk.Length];
                fs.Read(buff, 0, (int)chunk.Length);

                using var fileStream = File.Create(chunkFullName);
                fileStream.Write(buff, 0, (int)chunk.Length);
                fileStream.Close();

                yield return new ChunkFile(f.Root, new FileInfo(chunkFullName), hashValue);
            }
        }

        public BinaryFile Merge(IEnumerable<IChunkFile> chunksToJoin, FileInfo target)
        {
            var chunkStreams = chunksToJoin.Select(c => new FileStream(c.FullName, FileMode.Open, FileAccess.Read));
            var stream = new ConcatenatedStream(chunkStreams);

            var xx = chunksToJoin.Sum(a => a.Length);

            //var restorePath = Path.Combine(clf.FullName, "haha.exe");
            //using var fff = File.Create(restorePath);
            using var fff = target.Create();
            stream.CopyTo(fff);
            fff.Close();

            return new BinaryFile(null, target);
        }



    }
}
