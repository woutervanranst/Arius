using System.IO;
using System.Linq;
using Arius.Core.Models;

namespace Arius.Core.Services
{
    /// <summary>
    /// A chunker that simply wraps the BinaryFile in one single chunk
    /// </summary>
    internal class SimpleChunker : IChunker
    {
        public IChunkFile[] Chunk(BinaryFile item)
        {
            return new[] { item };
        }

        public BinaryFile Merge(IChunkFile[] chunksToJoin, FileInfo target)
        {
            File.Move(chunksToJoin.Single().FullName, target.FullName);

            return new BinaryFile(null, target);
        }
    }
}
