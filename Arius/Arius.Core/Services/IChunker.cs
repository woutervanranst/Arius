using System.IO;
using Arius.Core.Models;

namespace Arius.Core.Services
{
    internal interface IChunker
    {
        internal interface IOptions
        {
            bool Dedup { get; }
        }

        IChunkFile[] Chunk(BinaryFile fileToChunk);
        BinaryFile Merge(IChunkFile[] chunksToJoin, FileInfo target);
    }
}
