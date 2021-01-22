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


            //var streamBreaker = _sb; // new StreamBreaker();
            using var bffs = new FileStream(bf.FullName, FileMode.Open, FileAccess.Read);
            var chunkDefs = _sb.GetChunks(bffs, bffs.Length, SHA256.Create()).ToArray();


            //using var fs = new FileStream(bf.FullName, FileMode.Open, FileAccess.Read);
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




            //var tempDir = new DirectoryInfo(Path.Combine(_uploadTempDir.FullName, "chunks", $"{f.Name}.arius"));
            //if (tempDir.Exists)
            //    tempDir.Delete();
            //tempDir.Create();

            ////using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read);
            //////fs.Position = 0;

            ////using var hasher = SHA256.Create();

            ////var chunkDefs = _sb.GetChunks(fs, fs.Length, hasher).ToArray();
            //var cfs = new List<ChunkFile>();

            //StreamBreaker.Chunk[] chunkDefs;
            //using (var hasher = SHA256.Create())
            //{
            //    var streamBreaker = new StreamBreaker();
            //    using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read))
            //    {
            //        chunkDefs = streamBreaker.GetChunks(fs, fs.Length, hasher).ToArray();



            //        for (int i = 0; i < chunkDefs.Length; i++)
            //        //foreach (var chunk in chunkDefs)
            //        {
            //            //var hashValue = new HashValue { Value = SHA256Hasher.ByteArrayToString(chunk.Hash) };

            //            var chunk = chunkDefs[i];
            //            byte[] buff = new byte[chunk.Length];
            //            fs.Read(buff, 0, (int)chunk.Length);

            //            var chunkFullName = Path.Combine(tempDir.FullName, $"{i}{ChunkFile.Extension}");
            //            using var fileStream = File.Create(chunkFullName);
            //            fileStream.Write(buff, 0, (int)chunk.Length);
            //            fileStream.Close();

            //            var hashValue = _hvp.GetHashValue(chunkFullName);

            //            cfs.Add(new ChunkFile(f.Root, new FileInfo(chunkFullName), hashValue));
            //        }

            //        fs.Close();
            //    }
            //}


            //return cfs.ToArray();
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
