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

        public abstract IEnumerable<byte[]> Chunk(Stream streamToChunk);
        public virtual BinaryFile Merge(DirectoryInfo root, Stream[] chunks, FileInfo target)
        {
            if (chunks.Length == 0)
            {
                throw new ArgumentException("No chunks to merge", nameof(chunks));
            }
            else if (chunks.Length == 1)
            {
                var stream = chunks.Single();

                using (var targetStream = target.OpenWrite())
                {
                    stream.CopyTo(targetStream);
                }

                //chunk.Position = 0;
                var h = hvp.GetManifestHash(target.FullName);

                return new BinaryFile(root, target, h);
            }
            else
            {
                var stream = new ConcatenatedStream(chunks);

                using (var targetStream = target.Create()) // File.Create(target.FullName); // target.Create();
                {
                    stream.CopyTo(targetStream);
                }
                //targetStream.Close();

                var h = hvp.GetManifestHash(target.FullName);

                return new BinaryFile(root, target, h);
            }
        }
    }
}
