using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public IEnumerable<IChunkFile> Chunk(ILocalContentFile fileToChunk)
        {
            return new IChunkFile[] { (IChunkFile)fileToChunk };
        }

        public ILocalContentFile Merge(IEnumerable<IChunkFile> chunksToJoin)
        {
            return (ILocalContentFile)chunksToJoin.Single();
        }





        public IEnumerable<ChunkFile2> Chunk(BinaryFile item)
        {
            return new ChunkFile2[] {new(new FileInfo(item.FileFullName)) { Hash = item.Hash }};
        }
    }


    internal class DedupChunker : IChunker
    {
        public IEnumerable<IChunkFile> Chunk(ILocalContentFile fileToChunk)
        {
            throw new NotImplementedException();

//                //var sb = new StreamBreaker();

//                //using var fs = new FileStream(_fi.FullName, FileMode.Open, FileAccess.Read);
//                //var chunks = sb.GetChunks(fs, fs.Length, SHA256.Create()).ToImmutableArray();
//                //fs.Position = 0;

//                //DirectoryInfo tempDir = new DirectoryInfo(Path.Combine(_fi.Directory.FullName, _fi.Name + ".arius"));
//                //tempDir.Create();

//                //foreach (var chunk in chunks)
//                //{
//                //    var chunkFullName = Path.Combine(tempDir.FullName, BitConverter.ToString(chunk.Hash));

//                //    byte[] buff = new byte[chunk.Length];
//                //    fs.Read(buff, 0, (int)chunk.Length);

//                //    using var fileStream = File.Create(chunkFullName);
//                //    fileStream.Write(buff, 0, (int)chunk.Length);
//                //    fileStream.Close();
//                //}

//                //fs.Close();

//                //var laf = new LocalAriusManifest(this);
//                //var lac = chunks.Select(c => new LocalAriusChunk("")).ToImmutableArray();

//                //var r = new AriusFile(this, laf, lac);

//                //return r;
        }

        public ILocalContentFile Merge(IEnumerable<IChunkFile> chunksToJoin)
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
