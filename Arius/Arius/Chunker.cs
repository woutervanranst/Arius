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
    internal class Chunker : IChunker<LocalContentFile>
    {
        public IEnumerable<IChunk<LocalContentFile>> Chunk(LocalContentFile fileToChunk)
        {
            return new IChunk<LocalContentFile>[] { fileToChunk };
        }

        public LocalContentFile Merge(IEnumerable<IChunk<LocalContentFile>> chunksToJoin)
        {
            return (LocalContentFile)chunksToJoin.Single();
        }
    }

    internal class DedupChunker : IChunker<LocalContentFile>
    {
        public IEnumerable<IChunk<LocalContentFile>> Chunk(LocalContentFile fileToChunk)
        {
            throw new NotImplementedException();
        }

        public LocalContentFile Merge(IEnumerable<IChunk<LocalContentFile>> chunksToJoin)
        {
            throw new NotImplementedException();
        }
    }
}
