using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Models
{
    internal abstract class Chunk
    {
        public abstract Hash Hash { get; } 
    }

    internal class BinaryFileChunk : Chunk
    {
        public BinaryFileChunk(BinaryFile bf)
        {
            BinaryFile = bf;
        }

        public BinaryFile BinaryFile { get; }

        public override Hash Hash => BinaryFile.Hash;
    }

    internal class ByteArrayChunk : Chunk
    {
        public ByteArrayChunk(byte[] chunk, ChunkHash ch) //TODO quid memory allocation??
        {
            Bytes = chunk;
            Hash = ch;
        }
        
        public byte[] Bytes { get; }

        public override Hash Hash { get; }
    }
}
