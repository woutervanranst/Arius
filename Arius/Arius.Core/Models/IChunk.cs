using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Models
{
    internal interface IChunk
    {
        ChunkHash Hash { get; }
        Task<Stream> OpenReadAsync();
        Task<Stream> OpenWriteAsync();
    }

    internal interface IChunkFile : IFile, IChunk //TODO REMOVE?
    {
    }

    internal record MemoryChunk : IChunk
    {
        public MemoryChunk(byte[] chunk, ChunkHash ch) //TODO quid memory allocation??
        {
            Bytes = chunk;
            Hash = ch;
        }
        
        public byte[] Bytes { get; }

        public ChunkHash Hash { get; }

        public Task<Stream> OpenReadAsync() => Task.FromResult((Stream)new MemoryStream(Bytes, writable: false));
        public Task<Stream> OpenWriteAsync() => throw new InvalidOperationException(); // not supposed to write to this
    }

    internal class ChunkFile : FileBase, IChunkFile
    {
        public static readonly string Extension = ".ariuschunk";

        public ChunkFile(FileInfo fi, ChunkHash hash) : base(fi)
        {
            Hash = hash;
        }

        public override ChunkHash Hash { get; }

        public Task<Stream> OpenReadAsync() => Task.FromResult((Stream)File.OpenRead(fi.FullName));
        public Task<Stream> OpenWriteAsync() => Task.FromResult((Stream)File.Create(fi.FullName));
    }
}