//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using Arius.Core.Models;

//namespace Arius.Core.Services.Chunkers
//{
//    /// <summary>
//    /// A chunker that simply wraps the BinaryFile in one single chunk
//    /// </summary>
//    internal class SimpleChunker : Chunker
//    {
//        public SimpleChunker(IHashValueProvider hvp) : base(hvp)
//        { 
//        }

//        public override IChunkFile[] Chunk(BinaryFile item)
//        {
//            return new[] { item };
//        }

//        public override IEnumerable<byte[]> Chunk(Stream streamToChunk)
//        {
//            throw new System.NotImplementedException();
//        }
//    }
//}
