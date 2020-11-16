using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;

namespace Arius
{

    internal struct HashValue
    {
        public string Value { get; set; }
    }

    internal interface IHashValueProvider
    {
        HashValue GetHashValue(IHashable hashable);
    }

    internal interface ISHA256HasherOptions : ICommandExecutorOptions
    {
        public string Passphrase { get; }
    }

    internal class SHA256Hasher : IHashValueProvider
    {
        public SHA256Hasher(ICommandExecutorOptions options)
        {
            _salt = ((ISHA256HasherOptions)options).Passphrase;
        }

        private readonly string _salt;

        public HashValue GetHashValue(IHashable hashable)
        {
            return GetHashValue((dynamic) hashable);
        }

        private HashValue GetHashValue<T>(IPointerFile<T> hashable) where T : IHashable, IManifestBlob
        {
            return hashable.GetObject().Hash;
        }

        private HashValue GetHashValue(ILocalContentFile hashable)
        {
            byte[] byteArray = Encoding.ASCII.GetBytes(_salt);
            using Stream ss = new MemoryStream(byteArray);

            using Stream fs = System.IO.File.OpenRead(hashable.FullName);

            using var stream = new ConcatenatedStream(new Stream[] { ss, fs });
            using var sha256 = SHA256.Create();

            var hash = sha256.ComputeHash(stream);

            fs.Close();

            return new HashValue { Value = BitConverter.ToString(hash) };
        }
    }
}



