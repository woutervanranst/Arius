using Arius.Core.Domain;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Arius.Core.Infrastructure;

internal interface IHashValueProvider
{
    Task<Hash> GetHashAsync(BinaryFile bf);
    bool       IsValid(Hash h);
}

internal class SHA256Hasher : IHashValueProvider, IDisposable
{
    private readonly byte[] _saltBytes;
    private readonly SHA256 _sha256 = SHA256.Create();
    private const int BufferSize = 81920; // 80 KB buffer

    public SHA256Hasher(RepositoryOptions options) : this(options.Passphrase) { }
    public SHA256Hasher(string salt) : this(Encoding.ASCII.GetBytes(salt)) { }
    public SHA256Hasher(byte[] salt) => _saltBytes = salt;

    public async Task<Hash> GetHashAsync(BinaryFile bf)
    {
        var hashValue = await GetHashValueAsync(bf.FullName);
        return new Hash(hashValue);
    }

    public bool IsValid(Hash h) => h?.Value.Length == 32;

    private async Task<byte[]> GetHashValueAsync(string fullName)
    {
        using var       saltStream         = new MemoryStream(_saltBytes);
        await using var fileStream         = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var concatenatedStream = new OptimizedConcatenatedStream([saltStream, fileStream]);

        return await concatenatedStream.CalculateSHA256HashAsync(_sha256);
    }

    public void Dispose()
    {
        _sha256.Dispose();
    }
}

internal class OptimizedConcatenatedStream : Stream
{
    private readonly Queue<Stream> _streams;

    public OptimizedConcatenatedStream(IEnumerable<Stream> streams)
    {
        _streams = new Queue<Stream>(streams);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var totalBytesRead = 0;

        while (count > 0 && _streams.Count > 0)
        {
            var bytesRead = _streams.Peek().Read(buffer, offset, count);
            if (bytesRead == 0)
            {
                _streams.Dequeue().Dispose();
                continue;
            }

            totalBytesRead += bytesRead;
            offset += bytesRead;
            count -= bytesRead;
        }

        return totalBytesRead;
    }

    private const int BufferSize = 81920; // 80 KB buffer

    public async Task<byte[]> CalculateSHA256HashAsync(SHA256 sha256)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = Read(buffer, 0, buffer.Length)) > 0)
            {
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return sha256.Hash;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

//using System.Text;
//using Arius.Core.Domain;
//using Arius.Core.Domain.Storage;
//using Arius.Core.Domain.Storage.FileSystem;

//namespace Arius.Core.Infrastructure;

//internal interface IHashValueProvider  // TODO Move to Domain
//{
//    Task<Hash> GetHashAsync(BinaryFile bf);

//    //BinaryHash             GetBinaryHash(BinaryFileInfo bfi) => GetBinaryHash(bfi.FullName);
//    //BinaryHash             GetBinaryHash(FileInfo bfi)       => GetBinaryHash(bfi.FullName);
//    //BinaryHash             GetBinaryHash(string binaryFileFullName);
//    //async Task<BinaryHash> GetBinaryHashAsync(BinaryFileInfo bfi) => await GetBinaryHashAsync(bfi.FullName);
//    //async Task<BinaryHash> GetBinaryHashAsync(FileInfo bfi)       => await GetBinaryHashAsync(bfi.FullName);
//    //Task<BinaryHash>       GetBinaryHashAsync(string binaryFileFullName);

//    //ChunkHash GetChunkHash(string fullName);
//    //ChunkHash GetChunkHash(byte[] buffer);

//    bool IsValid(Hash h);
//}

//internal class SHA256Hasher : IHashValueProvider
//{
//    public SHA256Hasher(RepositoryOptions options) 
//        : this(options.Passphrase)
//    {
//    }
//    public SHA256Hasher(string salt)
//        : this(Encoding.ASCII.GetBytes(salt))
//    {
//    }
//    public SHA256Hasher(byte[] salt)
//    {
//        this.saltBytes = salt;
//    }
//    //public SHA256Hasher() 
//    //{
//    //    // DO NOT IMPLEMENT THIS ONE -- POTENTIAL TO CREATE HASHES WITHOUT SALT UNINTENTIONALLY
//    //    this.saltBytes = Array.Empty<byte>();
//    //}

//    private readonly byte[] saltBytes;

//    //public       BinaryHash       GetBinaryHash(string binaryFileFullName)      => new(GetHashValue(binaryFileFullName));
//    //public async Task<BinaryHash> GetBinaryHashAsync(string binaryFileFullName) => new(await GetHashValueAsync(binaryFileFullName));

//    //public ChunkHash GetChunkHash(string fullName) => new(GetHashValue(fullName));
//    //public ChunkHash GetChunkHash(byte[] buffer) => new(GetHashValue(buffer));

//    //TODO what with in place update of binary file (hash changed)?
//    // TODO what with lastmodifieddate changed but not hash?

//    //        //LastWriteTime does not match
//    //        var h = GetHashValue(bf.FullName);

//    //        if (pf.Hash == h)
//    //        {
//    //            //LastWriteTime was modified but the hash did not change. Update the LastWriteTime
//    //            File.SetLastWriteTimeUtc(pf.FullName, File.GetLastWriteTimeUtc(bf.FullName));

//    //            logger.LogWarning($"Using fasthash for {bf.RelativeName}. LastWriteTime of PointerFile was out of sync with BinaryFile. Corrected."); //TODO does this get reflected in the PoitnerFileENtry?

//    //            return h;
//    //        }
//    //        else
//    //        {
//    //            //LastWriteTime was modified AND the hash changed.
//    //            logger.LogError($"Using fasthash for {bf.RelativeName}. Hash out of sync.");

//    //            throw new NotImplementedException(); //TODO what if the binaryfile was modified in place?!
//    //        }
//    //    }
//    //}

//    public async Task<Hash> GetHashAsync(BinaryFile bf)
//    {
//        return new Hash(await GetHashValueAsync(bf.FullName));
//    }

//    public bool IsValid(Hash h)
//    {
//        if (h == null)
//            return false;

//        if (h.Value.Length != 32)
//            return false;

//        return true;

//        //return SHA265WordRegex().Match(h.Value.BytesToHexString()).Success;
//    }


//    //[GeneratedRegex("^[a-f0-9]{64}$")]
//    //private static partial Regex SHA265WordRegex(); //https://stackoverflow.com/a/6630280/1582323 with A-F removed since we do .ToLower() in BytesToString

//    private byte[] GetHashValue(string fullName) // WARNING byte arrays are value types - two arrays with the same content are not equal -- do not make this method public or beware of quirky bugs
//    {
//        using var saltStream   = new MemoryStream(saltBytes);
//        using var fs           = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
//        using var saltedStream = new ConcatenatedStream(new Stream[] { saltStream, fs });

//        return saltedStream.CalculateSHA256Hash();
//    }
//    private async Task<byte[]> GetHashValueAsync(string fullName) // WARNING byte arrays are value types - two arrays with the same content are not equal -- do not make this method public or beware of quirky bugs
//    {
//        using var       saltStream   = new MemoryStream(saltBytes);
//        await using var fs           = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
//        await using var saltedStream = new ConcatenatedStream(new Stream[] { saltStream, fs });

//        return await saltedStream.CalculateSHA256HashAsync();
//    }
//    private byte[] GetHashValue(byte[] bytes) // WARNING byte arrays are value types - two arrays with the same content are not equal -- do not make this method public or beware of quirky bugs
//    {
//        using var saltStream   = new MemoryStream(saltBytes);
//        using var s            = new MemoryStream(bytes);
//        using var saltedStream = new ConcatenatedStream(new Stream[] { saltStream, s });

//        return saltedStream.CalculateSHA256Hash();
//    }


//    private class ConcatenatedStream : Stream
//    {
//        public ConcatenatedStream(IEnumerable<Stream> streams)
//        {
//            this.streams = new(streams);
//        }
//        private readonly Queue<Stream> streams;

//        public override bool CanRead => true;

//        public override int Read(byte[] buffer, int offset, int count)
//        {
//            int totalBytesRead = 0;

//            while (count > 0 && streams.Count > 0)
//            {
//                int bytesRead = streams.Peek().Read(buffer, offset, count);
//                if (bytesRead == 0)
//                {
//                    streams.Dequeue().Dispose();
//                    continue;
//                }

//                totalBytesRead += bytesRead;
//                offset         += bytesRead;
//                count          -= bytesRead;
//            }

//            return totalBytesRead;
//        }

//        public override bool CanSeek                                     => false;
//        public override bool CanWrite                                    => false;
//        public override long Length                                      => throw new NotImplementedException();
//        public override long Position                                    { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
//        public override void Flush()                                     => throw new NotImplementedException();
//        public override long Seek(long offset, SeekOrigin origin)        => throw new NotImplementedException();
//        public override void SetLength(long value)                       => throw new NotImplementedException();
//        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
//    }
//}