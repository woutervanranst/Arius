using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Models
{
    internal interface IChunk
    {
        ChunkHash Hash { get; }
        Stream GetStream();
    }

    internal interface IChunkFile : IFile, IChunk
    {
    }

    internal record MemoryChunk : IChunk
    {
        public MemoryChunk(byte[] chunk, ChunkHash ch) //TODO quid memory allocation??
        {
            Bytes = chunk;
            Hash = ch;
        }
        
        public byte[] Bytes { get; }

        public ChunkHash Hash { get; }

        public Stream GetStream() => new MemoryStream(Bytes);
    }

    internal class ChunkFile : FileBase, IChunkFile
    {
        public static readonly string Extension = ".chunk.arius";

        public ChunkFile(FileInfo fi, ChunkHash hash) : base(fi)
        {
            Hash = hash;
        }

        public override ChunkHash Hash { get; }

        public Stream GetStream() => File.OpenRead(fi.FullName);
    }
}