using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;

namespace Arius.Services
{
    internal struct HashValue
    {
        public string Value { get; set; }
        public override string ToString() => Value;


        //public static bool operator ==(HashValue c1, HashValue c2)
        //{
        //    return c1.Equals(c2);
        //}

        //public static bool operator !=(HashValue c1, HashValue c2)
        //{
        //    return !c1.Equals(c2);
        //}

        //public override bool Equals(object obj)
        //{
        //    // If parameter is null return false.
        //    if (obj == null)
        //        return false;

        //    // If parameter cannot be cast to Point return false.
        //    var p = obj as HashValue;
        //    if (p == null)
        //    {
        //        return false;
        //    }

        //    // Return true if the fields match:
        //    return (x == p.x) && (y == p.y);
        //}
    }

    
    internal interface ISHA256HasherOptions : ICommandExecutorOptions
    {
        public string Passphrase { get; }
        public bool FastHash { get; }
    }

    internal class SHA256Hasher : IHashValueProvider
    {
        public SHA256Hasher(ICommandExecutorOptions options)
        {
            var o = (ISHA256HasherOptions) options;
            _salt = o.Passphrase;
            _fastHash = o.FastHash;
        }

        private readonly string _salt;
        private readonly bool _fastHash;

        public HashValue GetHashValue(BinaryFile f)
        {
            var h = default(HashValue?);

            if (_fastHash)
            {
                var pointerFileInfo = f.PointerFileInfo;
                if (pointerFileInfo.Exists)
                {
                    var pf = new PointerFile(f.Root, pointerFileInfo);
                    h = pf.Hash;
                }
            }

            if (!h.HasValue)
                h = GetHashValue2(f);

            return h.Value;
        }

        private HashValue GetHashValue2(BinaryFile f)
        {
            byte[] byteArray = Encoding.ASCII.GetBytes(_salt);
            using Stream ss = new MemoryStream(byteArray);

            using Stream fs = System.IO.File.OpenRead(f.FullName);

            using var stream = new ConcatenatedStream(new Stream[] { ss, fs });
            using var sha256 = SHA256.Create();

            var hash = sha256.ComputeHash(stream);

            fs.Close();

            return new HashValue {Value = ByteArrayToString(hash) }; // Encoding.UTF8.GetString(hash)}; // BitConverter.ToString(hash) };
        }

        public static string ByteArrayToString(byte[] ba)
        {
            // https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa?page=1&tab=votes#tab-top

            //StringBuilder hex = new StringBuilder(ba.Length * 2);
            //foreach (byte b in ba)
            //    hex.AppendFormat("{0:x2}", b);
            //return hex.ToString();

            return BitConverter.ToString(ba).Replace("-", "").ToLower();
        }
    }
}



