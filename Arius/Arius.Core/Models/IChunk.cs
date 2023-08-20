using System;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Core.Models;

internal interface IChunk
{
    public long Length { get; }
    ChunkHash ChunkHash { get; }
    Task<Stream> OpenReadAsync();
}

internal record MemoryChunk : IChunk
{
    public MemoryChunk(byte[] chunk, ChunkHash ch) //TODO quid memory allocation??
    {
        Bytes = chunk;
        ChunkHash = ch;
    }
        
    public byte[] Bytes  { get; }

    public long Length => Bytes.Length;

    public ChunkHash ChunkHash { get; }

    public Task<Stream> OpenReadAsync() => Task.FromResult((Stream)new MemoryStream(Bytes, writable: false));
}