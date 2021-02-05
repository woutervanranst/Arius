using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Services
{
    internal interface IHashValueProvider
    {
        HashValue GetHashValue(BinaryFile bf);
        HashValue GetHashValue(string fullName);
    }


    internal interface ISHA256HasherOptions : ICommandExecutorOptions
    {
        public string Passphrase { get; }
        public bool FastHash { get; }
    }

    
    internal class SHA256Hasher : IHashValueProvider
    {
        public SHA256Hasher(ILogger<SHA256Hasher> logger, ICommandExecutorOptions options)
        {
            var o = (ISHA256HasherOptions) options;
            _salt = o.Passphrase;
            _fastHash = o.FastHash;
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
            byte[] byteArray = Encoding.ASCII.GetBytes(_salt);
            using Stream ss = new MemoryStream(byteArray);

            using Stream fs = File.OpenRead(fullName);

            using var stream = new ConcatenatedStream(new Stream[] { ss, fs });
            using var sha256 = SHA256.Create();

            var hash = sha256.ComputeHash(stream);

            fs.Close();

            return new HashValue { Value = ByteArrayToString(hash) }; // Encoding.UTF8.GetString(hash)}; // BitConverter.ToString(hash) };
        }

        public static string ByteArrayToString(byte[] ba)
        {
            // https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa?page=1&tab=votes#tab-top

            //StringBuilder hex = new StringBuilder(ba.Length * 2);
            //foreach (byte b in ba)
            //    hex.AppendFormat("{0:x2}", b);
            //return hex.ToString();

            return Convert.ToHexString(ba).ToLower();

            //return BitConverter.ToString(ba).Replace("-", "").ToLower();
        }
    }
}



