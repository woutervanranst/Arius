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
        protected Chunker(IHashValueProvider hashValueProvider)
        {
            this.hashValueProvider = hashValueProvider;
        }

        protected readonly IHashValueProvider hashValueProvider;

        public abstract IEnumerable<Chunk> Chunk(Stream streamToChunk);
        public virtual BinaryFile Merge(DirectoryInfo root, Chunk[] chunks, FileInfo target)
        {
            if (chunks.Length == 0)
            {
                throw new ArgumentException("No chunks to merge", nameof(chunks));
            }
            else if (chunks.Length == 1)
            {
                var chunk = chunks.Single();

                using (var sourceStream = ChunkToStream(chunk))
                {
                    using (var targetStream = target.OpenWrite())
                    {
                        sourceStream.CopyTo(targetStream);
                    }
                }

                //chunk.Position = 0;
                var h = hashValueProvider.GetManifestHash(target.FullName);

                return new BinaryFile(root, target, h);
            }
            else
            {
                var stream = new ConcatenatedStream(chunks.Select(chunk => ChunkToStream(chunk)));

                using (var targetStream = target.Create()) // File.Create(target.FullName); // target.Create();
                {
                    stream.CopyTo(targetStream);
                }
                //targetStream.Close();

                var h = hashValueProvider.GetManifestHash(target.FullName);

                return new BinaryFile(root, target, h);
            }
        }

        private static Stream ChunkToStream(Chunk chunk)
        {
            if (chunk is ByteArrayChunk bac)
                return new MemoryStream(bac.Bytes);
            else if (chunk is BinaryFileChunk bfc)
                return File.OpenRead(bfc.BinaryFile.FullName);
            else
                throw new ArgumentException();
        }
    }
}
