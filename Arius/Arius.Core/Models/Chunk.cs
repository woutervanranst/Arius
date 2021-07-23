using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Models
{
    internal abstract record Chunk
    {
        public abstract ChunkHash Hash { get; }
        public abstract Stream GetStream();
    }

    internal record BinaryFileChunk : Chunk
    {
        public BinaryFileChunk(BinaryFile bf)
        {
            BinaryFile = bf;
            Hash = new(BinaryFile.Hash);
        }

        public BinaryFile BinaryFile { get; }

        public override ChunkHash Hash { get; }

        public override Stream GetStream() => File.OpenRead(BinaryFile.FullName);
    }

    internal record ByteArrayChunk : Chunk
    {
        public ByteArrayChunk(byte[] chunk, ChunkHash ch) //TODO quid memory allocation??
        {
            Bytes = chunk;
            Hash = ch;
        }
        
        public byte[] Bytes { get; }

        public override ChunkHash Hash { get; }

        public override Stream GetStream() => new MemoryStream(Bytes);
    }
}