using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Arius.Core.Domain;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Infrastructure.Services;

public class SHA256Hasher : IHashValueProvider, IDisposable
{
    private readonly byte[]              saltBytes;
    private readonly ThreadLocal<SHA256> sha256      = new(SHA256.Create);
    private const    int                 BUFFER_SIZE = 81920; // 80 KB buffer

    public SHA256Hasher(RepositoryOptions options) : this(options.Passphrase) { }
    public SHA256Hasher(string salt) : this(Encoding.ASCII.GetBytes(salt)) { }
    public SHA256Hasher(byte[] salt) => saltBytes = salt;
    //public SHA256Hasher() // DO NOT IMPLEMENT THIS ONE -- POTENTIAL TO CREATE HASHES WITHOUT SALT UNINTENTIONALLY

    public async Task<Hash> GetHashAsync(BinaryFile bf)
    {
        var hashValue = await GetHashValueAsync(bf.FullName);
        return new Hash(hashValue);
    }

    public bool IsValid(Hash h) => h?.Value.Length == 32;

    private async Task<byte[]> GetHashValueAsync(string fullName)
    {
        using var       saltStream   = new MemoryStream(saltBytes);
        await using var fileStream   = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var saltedStream = new ConcatenatedStream([saltStream, fileStream]);

        return CalculateSha256Hash(saltedStream);
    }

    private byte[] CalculateSha256Hash(Stream s) // WARNING byte arrays are value types - two arrays with the same content are not equal -- do not make this method public or beware of quirky bugs
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
        try
        {
            int bytesRead;
            while ((bytesRead = s.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha256.Value.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            sha256.Value.TransformFinalBlock([], 0, 0);
            return sha256.Value.Hash;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        if (sha256.IsValueCreated)
        {
            sha256.Value.Dispose();
        }
        sha256.Dispose();
    }


    private class ConcatenatedStream : Stream
    {
        private readonly Queue<Stream> streams;

        public ConcatenatedStream(IEnumerable<Stream> streams)
        {
            this.streams = new Queue<Stream>(streams);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var totalBytesRead = 0;

            while (count > 0 && streams.Count > 0)
            {
                var bytesRead = streams.Peek().Read(buffer, offset, count);
                if (bytesRead == 0)
                {
                    streams.Dequeue().Dispose();
                    continue;
                }

                totalBytesRead += bytesRead;
                offset += bytesRead;
                count -= bytesRead;
            }

            return totalBytesRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}


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


//    public bool IsValid(Hash h)
//        //return SHA265WordRegex().Match(h.Value.BytesToHexString()).Success;
//    }

//    //[GeneratedRegex("^[a-f0-9]{64}$")]
//    //private static partial Regex SHA265WordRegex(); //https://stackoverflow.com/a/6630280/1582323 with A-F removed since we do .ToLower() in BytesToString

//    private byte[] GetHashValue(byte[] bytes) // WARNING byte arrays are value types - two arrays with the same content are not equal -- do not make this method public or beware of quirky bugs
//    {
//        using var saltStream   = new MemoryStream(saltBytes);
//        using var s            = new MemoryStream(bytes);
//        using var saltedStream = new ConcatenatedStream(new Stream[] { saltStream, s });

//        return saltedStream.CalculateSHA256Hash();
//    }