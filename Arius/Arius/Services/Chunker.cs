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
        public IChunkFile[] Chunk(BinaryFile item)
        {
            return new IChunkFile[] {item};

            //                return new ChunkFile2[] 
            //{ 
            //    new(new FileInfo(item.FileFullName))
            //    {
            //        Hash = item.Hash
            //    }
            //};
        }

        public BinaryFile Merge(IChunkFile[] chunksToJoin)
        {
            return new BinaryFile(new FileInfo(chunksToJoin.Single().FullName));
        }
    }


    internal class DedupChunker : IChunker
    {
        public IChunkFile[] Chunk(BinaryFile f)
        {
            //throw new NotImplementedException();

            var sb = new StreamBreaker();

            using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read);
            var chunks = sb.GetChunks(fs, fs.Length, SHA256.Create()).ToImmutableArray();
            fs.Position = 0;

            DirectoryInfo tempDir = new DirectoryInfo(Path.Combine(f.Directory.FullName, f.Name + ".arius"));
            tempDir.Create();

            var chunkFiles = new List<ChunkFile2>();

            foreach (var chunk in chunks)
            {
                var hashValue = new HashValue {Value = SHA256Hasher.ByteArrayToString(chunk.Hash)};
                
                var chunkFullName = Path.Combine(tempDir.FullName, hashValue.Value + ChunkFile2.Extension);

                byte[] buff = new byte[chunk.Length];
                fs.Read(buff, 0, (int)chunk.Length);

                using var fileStream = File.Create(chunkFullName);
                fileStream.Write(buff, 0, (int)chunk.Length);
                fileStream.Close();

                chunkFiles.Add(new ChunkFile2(new FileInfo(chunkFullName)){ Hash = hashValue });
            }

            return chunkFiles.ToArray();
        }

        public BinaryFile Merge(IChunkFile[] chunksToJoin)
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
