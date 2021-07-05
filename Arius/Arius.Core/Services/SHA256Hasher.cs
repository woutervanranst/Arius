using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;
using static Arius.Core.Facade.Facade;

namespace Arius.Core.Services
{
    internal interface IHashValueProvider
    {
        internal interface IOptions
        {
            string Passphrase { get; }
        }

        ManifestHash GetManifestHash(FileInfo bfi);
        ManifestHash GetManifestHash(string binaryFileFullName);
        ChunkHash GetChunkHash(string fullName);
        ChunkHash GetChunkHash(Stream stream);
    }

    internal class SHA256Hasher : IHashValueProvider
    {
        public SHA256Hasher(ILogger<SHA256Hasher> logger, IHashValueProvider.IOptions options)
        {
            this.logger = logger;
            salt = options.Passphrase;
        }

        private readonly string salt;
        private readonly ILogger<SHA256Hasher> logger;

        public ManifestHash GetManifestHash(FileInfo bfi) => GetManifestHash(bfi.FullName);
        public ManifestHash GetManifestHash(string fullName) => new(GetHashValue(fullName, salt));
        public ChunkHash GetChunkHash(string fullName) => new(GetHashValue(fullName, salt));
        public ChunkHash GetChunkHash(Stream stream) => new(GetHashValue(stream, salt));
        ///// <summary>
        ///// Get the HashValue for the given BinaryFile.
        ///// If the FastHash option is set and a corresponding PointerFile with the same LastWriteTime is found, return the hash from the PointerFile
        ///// </summary>
        ///// <param name="bf"></param>
        ///// <returns></returns>
        //public ManifestHash GetManifestHash(BinaryFile bf)
        //{
        //    if (fastHash &&
        //        PointerService.GetPointerFile(bf) is var pf && pf is not null)
        //    {
        //        //A corresponding PointerFile exists
        //        logger.LogDebug($"Using fasthash for {bf.RelativeName}");

        //        return pf.Hash;
        //    }

        //    return GetManifestHash(bf.FullName);


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


        internal static string GetHashValue(string fullName, string salt)
        {
            using var fs = File.OpenRead(fullName);

            return GetHashValue(fs, salt);
        }

        private static string GetHashValue(Stream stream, string salt)
        {
            var byteArray = Encoding.ASCII.GetBytes(salt);
            using var ss = new MemoryStream(byteArray);

            using var stream2 = new ConcatenatedStream(new Stream[] { ss, stream });
            using var sha256 = SHA256.Create(); //not thread safe

            var hash = sha256.ComputeHash(stream2);

            return ByteArrayToString(hash);
        }


        private static string ByteArrayToString(byte[] ba) => Convert.ToHexString(ba).ToLower();



        //public async Task<ManifestHash> GetManifestHash(FileInfo bfi) => await GetManifestHash(bfi.FullName);
        //public async Task<ManifestHash> GetManifestHash(string binaryFileFullName) => new(await GetHashValue(binaryFileFullName, salt));
        //public async Task<ChunkHash> GetChunkHash(string fullName) => new(await GetHashValue(fullName, salt));
        //public async Task<ChunkHash> GetChunkHash(Stream stream) => new(await GetHashValue(stream, salt));

        //internal static async Task<string> GetHashValue(string fullName, string salt)
        //{
        //    using var fs = File.OpenRead(fullName);

        //    return await GetHashValue(fs, salt);
        //}

        //private static async Task<string> GetHashValue(Stream stream, string salt)
        //{
        //    var byteArray = Encoding.ASCII.GetBytes(salt);
        //    using var ss = new MemoryStream(byteArray);

        //    using var stream2 = new ConcatenatedStream(new Stream[] { ss, stream });
        //    using var sha256 = SHA256.Create(); //not thread safe

        //    var hash = await sha256.ComputeHashAsync(stream2);

        //    return ByteArrayToString(hash);
        //}

    }
}



