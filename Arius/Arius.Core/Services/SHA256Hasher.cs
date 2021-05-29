using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Arius.Core.Commands;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arius.Core.Services
{
    internal interface IHashValueProvider
    {
        internal class Options
        {
            public string Passphrase { get; init; }
            public bool FastHash { get; init; }
        }

        HashValue GetHashValue(BinaryFile bf);
        HashValue GetHashValue(string fullName);
    }

    internal class SHA256Hasher : IHashValueProvider
    {
        public SHA256Hasher(IOptions<IHashValueProvider.Options> options, ILogger<SHA256Hasher> logger)
        {
            _salt = options.Value.Passphrase;
            _fastHash = options.Value.FastHash;
            _logger = logger;
        }

        private readonly string _salt;
        private readonly bool _fastHash;
        private readonly ILogger<SHA256Hasher> _logger;

        public HashValue GetHashValue(BinaryFile bf)
        {
            var h = default(HashValue?);

            if (_fastHash)
            {
                var pointerFileInfo = bf.PointerFileInfo;
                if (pointerFileInfo.Exists &&
                    pointerFileInfo.LastWriteTimeUtc == File.GetLastWriteTimeUtc(bf.FullName))
                {
                    _logger.LogDebug($"Using fasthash for {bf.RelativeName}");

                    var pf = new PointerFile(bf.Root, pointerFileInfo);
                    h = pf.Hash;
                }
            }

            if (!h.HasValue)
                h = GetHashValue(bf.FullName);

            return h.Value;
        }

        public HashValue GetHashValue(string fullName)
        {
            return new HashValue { Value = GetHashValue(fullName, _salt) };
        }

        public static string GetHashValue(string fullName, string salt)
        {
            var byteArray = Encoding.ASCII.GetBytes(salt);
            using Stream ss = new MemoryStream(byteArray);

            using Stream fs = File.OpenRead(fullName);

            using var stream = new ConcatenatedStream(new Stream[] { ss, fs });
            using var sha256 = SHA256.Create();

            var hash = sha256.ComputeHash(stream);

            fs.Close();

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



