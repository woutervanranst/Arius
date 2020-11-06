using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Arius
{
    class FileUtils
    {
        //public static string GetHash(string salt, string fileName)
        //{
        //    byte[] byteArray = Encoding.ASCII.GetBytes(salt);
        //    using Stream ss = new MemoryStream(byteArray);

        //    using Stream fs = File.OpenRead(fileName);

        //    using var stream = new ConcatenatedStream(new Stream[] { ss, fs });

        //    using var md5 = MD5.Create();
        //    var hash = md5.ComputeHash(stream);

        //    return BitConverter.ToString(hash);
        //}
    }
}
