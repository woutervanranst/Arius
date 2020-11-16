//using System;
//using System.IO;
//using System.Security.Cryptography;
//using System.Text;

//namespace Arius
//{
//    internal class FileUtils
//    {
//        //public static string GetHash(string fileName)
//        //{
//        //    using Stream fs = File.OpenRead(fileName);
//        //    using var sha256 = SHA256.Create();

//        //    var hash = sha256.ComputeHash(fs);

//        //    fs.Close();

//        //    return BitConverter.ToString(hash);
//        //}
//        public static string GetHash(string salt, string fileName)
//        {
//            byte[] byteArray = Encoding.ASCII.GetBytes(salt);
//            using Stream ss = new MemoryStream(byteArray);

//            using Stream fs = File.OpenRead(fileName);

//            using var stream = new ConcatenatedStream(new Stream[] { ss, fs });
//            using var sha256 = SHA256.Create();

//            var hash = sha256.ComputeHash(stream);

//            fs.Close();

//            return BitConverter.ToString(hash);
//        }
//    }
//}
