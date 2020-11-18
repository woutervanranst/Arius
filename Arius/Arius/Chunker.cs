using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;

namespace Arius
{
    internal interface IChunkerOptions : ICommandExecutorOptions
    {
        bool Dedup { get; }
    }
    internal class Chunker : IChunker<ILocalContentFile>
    {
        public IEnumerable<IChunk<ILocalContentFile>> Chunk(ILocalContentFile fileToChunk)
        {
            return new IChunk<ILocalContentFile>[] { (IChunk<ILocalContentFile>)fileToChunk };
        }

        public ILocalContentFile Merge(IEnumerable<IChunk<ILocalContentFile>> chunksToJoin)
        {
            return (ILocalContentFile)chunksToJoin.Single();
        }
    }

    internal class DedupChunker : IChunker<ILocalContentFile>
    {
        public IEnumerable<IChunk<ILocalContentFile>> Chunk(ILocalContentFile fileToChunk)
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

        public ILocalContentFile Merge(IEnumerable<IChunk<ILocalContentFile>> chunksToJoin)
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
