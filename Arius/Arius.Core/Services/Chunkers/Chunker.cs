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

    //public async Task<BinaryFile> MergeAsync(DirectoryInfo root, IChunk[] chunks, FileInfo target)
    //{
    //    if (chunks.Length == 0)
    //        throw new ArgumentException("No chunks to merge", nameof(chunks));

    //    var chs = await Task.WhenAll(chunks.Select(async chunk => await chunk.OpenReadAsync()));
    //    var stream = new ConcatenatedStream(chs);

    //    using (var targetStream = target.Create())
    //    {
    //        await stream.CopyToAsync(targetStream);
    //    }

    //    var h = hashValueProvider.GetBinaryHash(target.FullName);

    //    return new BinaryFile(root, target, h);
    //}
}