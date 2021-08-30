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

    //internal record BinaryFileChunk : IChunk
    //{
    //    public BinaryFileChunk(BinaryFile bf)
    //    {
    //        BinaryFile = bf;
    //        Hash = new(BinaryFile.Hash);
    //    }

    //    public BinaryFile BinaryFile { get; }

    //    public ChunkHash Hash { get; }

    //    public Stream GetStream() => File.OpenRead(BinaryFile.FullName);
    //}

    internal record ByteArrayChunk : IChunk
    {
        public ByteArrayChunk(byte[] chunk, ChunkHash ch) //TODO quid memory allocation??
        {
            Bytes = chunk;
            Hash = ch;
        }
        
        public byte[] Bytes { get; }

        public ChunkHash Hash { get; }

        public Stream GetStream() => new MemoryStream(Bytes);
    }
}