using System.Collections.Generic;
using System.IO;
using Arius.Core.Models;

namespace Arius.Core.Services.Chunkers;

internal abstract class Chunker
{
    protected Chunker(IHashValueProvider hashValueProvider)
    {
        this.hashValueProvider = hashValueProvider;
    }

    protected readonly IHashValueProvider hashValueProvider;

    public abstract IEnumerable<IChunk> Chunk(Stream streamToChunk);
}