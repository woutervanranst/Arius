﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Models
{
    internal record ManifestProperties
    {
        public ManifestHash Hash { get; init; }
        public long OriginalLength { get; init; }
        public long ArchivedLength { get; init; }
        public int ChunkCount { get; init; }
    }
}