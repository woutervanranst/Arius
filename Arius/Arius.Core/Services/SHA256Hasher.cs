using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Extensions;
using Arius.Core.Facade;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Services;

internal interface IHashValueProvider
{
    BinaryHash             GetBinaryHash(BinaryFileInfo bfi) => GetBinaryHash(bfi.FullName);
    BinaryHash             GetBinaryHash(FileInfo bfi)       => GetBinaryHash(bfi.FullName);
    BinaryHash             GetBinaryHash(string binaryFileFullName);
    async Task<BinaryHash> GetBinaryHashAsync(BinaryFileInfo bfi) => await GetBinaryHashAsync(bfi.FullName);
    async Task<BinaryHash> GetBinaryHashAsync(FileInfo bfi)       => await GetBinaryHashAsync(bfi.FullName);
    Task<BinaryHash>       GetBinaryHashAsync(string binaryFileFullName);

    ChunkHash GetChunkHash(string fullName);
    ChunkHash GetChunkHash(byte[] buffer);

    bool IsValid(Hash h);
}

internal partial class SHA256Hasher : IHashValueProvider
{
    public SHA256Hasher(IRepositoryOptions options) 
        : this(options.Passphrase)
    {
    }
    public SHA256Hasher(string salt)
        : this(Encoding.ASCII.GetBytes(salt))
    {
    }
    public SHA256Hasher(byte[] salt)
    {
        this.saltBytes = salt;
    }
    //public SHA256Hasher() 
    //{
    //    // DO NOT IMPLEMENT THIS ONE -- POTENTIAL TO CREATE HASHES WITHOUT SALT UNINTENTIONALLY
    //    this.saltBytes = Array.Empty<byte>();
    //}

    private readonly byte[] saltBytes;

    public       BinaryHash       GetBinaryHash(string binaryFileFullName)      => new(GetHashValue(binaryFileFullName));
    public async Task<BinaryHash> GetBinaryHashAsync(string binaryFileFullName) => new(await GetHashValueAsync(binaryFileFullName));

    public ChunkHash GetChunkHash(string fullName) => new(GetHashValue(fullName));
    public ChunkHash GetChunkHash(byte[] buffer) => new(GetHashValue(buffer));
        
    //TODO what with in place update of binary file (hash changed)?
    // TODO what with lastmodifieddate changed but not hash?

    //        //LastWriteTime does not match
    //        var h = GetHashValue(bf.FullName);

    //        if (pf.Hash == h)
    //        {
    //            //LastWriteTime was modified but the hash did not change. Update the LastWriteTime
    //            File.SetLastWriteTimeUtc(pf.FullName, File.GetLastWriteTimeUtc(bf.FullName));

    //            logger.LogWarning($"Using fasthash for {bf.RelativeName}. LastWriteTime of PointerFile was out of sync with BinaryFile. Corrected."); //TODO does this get reflected in the PoitnerFileENtry?

    //            return h;
    //        }
    //        else
    //        {
    //            //LastWriteTime was modified AND the hash changed.
    //            logger.LogError($"Using fasthash for {bf.RelativeName}. Hash out of sync.");

    //            throw new NotImplementedException(); //TODO what if the binaryfile was modified in place?!
    //        }
    //    }
    //}

    public bool IsValid(Hash h)
    {
        if (h == null)
            return false;

        if (h.Value.Length != 32)
            return false;

        return true;

        //return SHA265WordRegex().Match(h.Value.BytesToHexString()).Success;
    }


    //[GeneratedRegex("^[a-f0-9]{64}$")]
    //private static partial Regex SHA265WordRegex(); //https://stackoverflow.com/a/6630280/1582323 with A-F removed since we do .ToLower() in BytesToString


    private byte[] GetHashValue(string fullName) // WARNING byte arrays are value types - two arrays with the same content are not equal -- do not make this method public or beware of quirky bugs
    {
        using var saltStream   = new MemoryStream(saltBytes);
        using var fs           = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        using var saltedStream = new ConcatenatedStream(new Stream[] { saltStream, fs });

        return saltedStream.CalculateSHA256Hash();
    }
    private async Task<byte[]> GetHashValueAsync(string fullName) // WARNING byte arrays are value types - two arrays with the same content are not equal -- do not make this method public or beware of quirky bugs
    {
        using var       saltStream   = new MemoryStream(saltBytes);
        await using var fs           = new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using var saltedStream = new ConcatenatedStream(new Stream[] { saltStream, fs });

        return await saltedStream.CalculateSHA256HashAsync();
    }
    private byte[] GetHashValue(byte[] bytes) // WARNING byte arrays are value types - two arrays with the same content are not equal -- do not make this method public or beware of quirky bugs
    {
        using var saltStream   = new MemoryStream(saltBytes);
        using var s            = new MemoryStream(bytes);
        using var saltedStream = new ConcatenatedStream(new Stream[] { saltStream, s });

        return saltedStream.CalculateSHA256Hash();
    }
}