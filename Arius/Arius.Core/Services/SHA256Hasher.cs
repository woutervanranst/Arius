using System;
using System.IO;
using System.Linq;
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

        BinaryHash GetBinaryHash(FileInfo bfi);
        BinaryHash GetBinaryHash(string binaryFileFullName);
        
        ChunkHash GetChunkHash(string fullName);
        ChunkHash GetChunkHash(byte[] buffer);
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

        public BinaryHash GetBinaryHash(FileInfo bfi) => GetBinaryHash(bfi.FullName);
        public BinaryHash GetBinaryHash(string binaryFileFullName) => new(GetHashValue(binaryFileFullName, salt));

        public ChunkHash GetChunkHash(string fullName) => new(GetHashValue(fullName, salt));
        public ChunkHash GetChunkHash(byte[] buffer) => new(GetHashValue(buffer, salt));
        
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
            var saltBytes = Encoding.ASCII.GetBytes(salt);
            using var saltStream = new MemoryStream(saltBytes);

            using var saltedStream = new ConcatenatedStream(new Stream[] { saltStream, stream });
            using var sha256 = SHA256.Create(); //not thread safe so create a new instance each time

            var hash = sha256.ComputeHash(saltedStream);

            return BytesToString(hash);
        }

        private static string GetHashValue(byte[] bytes, string salt)
        {
            var saltBytes = Encoding.ASCII.GetBytes(salt);
            using var saltStream = new MemoryStream(saltBytes);

            using var stream = new MemoryStream(bytes);

            using var saltedStream = new ConcatenatedStream(new Stream[] { saltStream, stream });

            using var sha256 = SHA256.Create(); //not thread safe so create a new instance each time

            var hash = sha256.ComputeHash(saltedStream);

            return BytesToString(hash);
        }

        private static string BytesToString(byte[] ba) => Convert.ToHexString(ba).ToLower();
    }
}