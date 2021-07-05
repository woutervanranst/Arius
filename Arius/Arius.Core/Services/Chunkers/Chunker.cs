using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arius.Core.Extensions;
using Arius.Core.Models;

namespace Arius.Core.Services.Chunkers
{
    internal abstract class Chunker
    {
        protected Chunker(IHashValueProvider hvp)
        {
            this.hvp = hvp;
        }

        protected readonly IHashValueProvider hvp;

        public abstract IChunkFile[] Chunk(BinaryFile fileToChunk);
        public virtual BinaryFile Merge(IChunkFile[] chunksToJoin, FileInfo target)
        {
            if (chunksToJoin.Length == 0)
            {
                throw new ArgumentException("No chunks to merge", nameof(chunksToJoin));
            }
            else if (chunksToJoin.Length == 1)
            {
                var chunk = chunksToJoin.Single();
                File.Move(chunk.FullName, target.FullName);

                return new BinaryFile(null, target, new ManifestHash(chunk.Hash.Value));
            }
            else
            {
                var chunkStreams = new List<Stream>();
                for (int i = 0; i < chunksToJoin.Length; i++)
                    chunkStreams.Add(new FileStream(chunksToJoin[i].FullName, FileMode.Open, FileAccess.Read));

                var stream = new ConcatenatedStream(chunkStreams);

                using var targetStream = File.Create(target.FullName); // target.Create();
                stream.CopyTo(targetStream);
                targetStream.Close();

                return new BinaryFile(null, target, hvp.GetManifestHash(target));
            }
        }
    }
}
