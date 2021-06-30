using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
            bool FastHash { get; }
        }

        HashValue GetHashValue(BinaryFile bf);
        HashValue GetHashValue(string fullName);
        HashValue GetHashValue(Stream stream);
    }

    internal class SHA256Hasher : IHashValueProvider
    {
        public SHA256Hasher(ILogger<SHA256Hasher> logger, IHashValueProvider.IOptions options)
        {
            this.logger = logger;
            salt = options.Passphrase;
            fastHash = options.FastHash;
        }

        private readonly string salt;
        private readonly bool fastHash;
        private readonly ILogger<SHA256Hasher> logger;

        /// <summary>
        /// Get the HashValue for the given BinaryFile.
        /// If the FastHash option is set and a corresponding PointerFile with the same LastWriteTime is found, return the hash from the PointerFile
        /// </summary>
        /// <param name="bf"></param>
        /// <returns></returns>
        public HashValue GetHashValue(BinaryFile bf)
        {
            if (fastHash &&
                PointerService.GetPointerFile(bf) is var pf && pf is not null)
            {
                //A corresponding PointerFile exists
                logger.LogDebug($"Using fasthash for {bf.RelativeName}");

                return pf.Hash;
            }

            return GetHashValue(bf.FullName);


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
        }

        public HashValue GetHashValue(string fullName)
        {
            return new HashValue { Value = GetHashValue(fullName, salt) };
        }

        public HashValue GetHashValue(Stream stream)
        {
            return new HashValue { Value = GetHashValue(stream, salt) };
        }

        internal static string GetHashValue(string fullName, string salt)
        {
            using var fs = File.OpenRead(fullName);

            return GetHashValue(fs, salt);
        }

        internal static string GetHashValue(Stream stream, string salt)
        {
            var byteArray = Encoding.ASCII.GetBytes(salt);
            using var ss = new MemoryStream(byteArray);

            using var stream2 = new ConcatenatedStream(new Stream[] { ss, stream });
            using var sha256 = SHA256.Create(); //not thread safe

            var hash = sha256.ComputeHash(stream2);

            return ByteArrayToString(hash);
        }


        private static string ByteArrayToString(byte[] ba)
        {
            // https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa?page=1&tab=votes#tab-top

            //StringBuilder hex = new StringBuilder(ba.Length * 2);
            //foreach (byte b in ba)
            //    hex.AppendFormat("{0:x2}", b);
            //return hex.ToString();

            // Encoding.UTF8.GetString(hash)}; // BitConverter.ToString(hash) };

            //return BitConverter.ToString(ba).Replace("-", "").ToLower();

            return Convert.ToHexString(ba).ToLower();
        }
    }
}



