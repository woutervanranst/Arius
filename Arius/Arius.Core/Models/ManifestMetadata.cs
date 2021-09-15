using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Models
{
    internal record ManifestMetadata
    {
        public BinaryHash Hash { get; init; }
        public long OriginalLength { get; init; }
        public long ArchivedLength { get; init; }
        public long IncrementalLength { get; init; }
        public int ChunkCount { get; init; }
    }
}
