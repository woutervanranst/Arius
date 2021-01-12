using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Arius.CommandLine;
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

        public BinaryFile Merge(IEnumerable<IChunkFile> chunksToJoin)
        {
            return new BinaryFile(new FileInfo(chunksToJoin.Single().FullName));
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
            
            var tempDir = new DirectoryInfo(Path.Combine(_config.TempDir.FullName, "chunks", f.Name + ".arius"));
            tempDir.Create();

            foreach (var chunk in _sb.GetChunks(fs, fs.Length, SHA256.Create()))
            {
                var hashValue = new HashValue {Value = SHA256Hasher.ByteArrayToString(chunk.Hash)};
                
                var chunkFullName = Path.Combine(tempDir.FullName, hashValue.Value + ChunkFile2.Extension);

                byte[] buff = new byte[chunk.Length];
                fs.Read(buff, 0, (int)chunk.Length);

                using var fileStream = File.Create(chunkFullName);
                fileStream.Write(buff, 0, (int)chunk.Length);
                fileStream.Close();

                yield return new ChunkFile2(new FileInfo(chunkFullName)){ Hash = hashValue };
            }
        }

        public BinaryFile Merge(IEnumerable<IChunkFile> chunksToJoin)
        {
            throw new NotImplementedException();

//        //var chunkFiles = chunks.Select(c => new FileStream(Path.Combine(clf.FullName, BitConverter.ToString(c.Hash)), FileMode.Open, FileAccess.Read));
//        //var concaten = new ConcatenatedStream(chunkFiles);

//        //var restorePath = Path.Combine(clf.FullName, "haha.exe");
//        //using var fff = File.Create(restorePath);
//        //concaten.CopyTo(fff);
//        //fff.Close();
        }

        
        
    }
}
