using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Permissions;
using System.Text;

namespace Arius
{
    class SevenZipUtils
    {
        static SevenZipUtils()
        {
            var lib = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Environment.Is64BitProcess ? "x64" : "x86", "7z.dll");
            SevenZip.SevenZipBase.SetLibraryPath(lib);
        }

        public void EncryptFile(string sourceFile, string targetFile, string password)
        {
            var compressor = new SevenZip.SevenZipCompressor
            {
                ArchiveFormat = SevenZip.OutArchiveFormat.SevenZip,
                CompressionLevel = SevenZip.CompressionLevel.None,
                EncryptHeaders = true,
                ZipEncryptionMethod = SevenZip.ZipEncryptionMethod.Aes256
            };

            compressor.CompressFilesEncrypted(targetFile, password, sourceFile);
        }

        //public void DecryptFile(string sourceFile, string targetFile, string password)
        //{
        //    var tfi = new FileInfo(targetFile);

        //    var extractor = new SevenZip.SevenZipExtractor(sourceFile, password);

        //    var di = Directory.CreateDirectory(Path.Combine(tfi.DirectoryName, Guid.NewGuid().ToString()));
        //    extractor.ExtractArchive(di.FullName);

        //    File.Move(Path.Combine(di.FullName, extractor.ArchiveFileNames.Single()), targetFile, true);

        //    if (di.EnumerateFiles().Any())
        //        throw new NotImplementedException(); // Folder should be empty but is not

        //    di.Delete();
        //}

        ////public string EncryptString(string text, string password)
        ////{
        ////    var compressor = new SevenZip.SevenZipCompressor();
        ////    compressor.ArchiveFormat = SevenZip.OutArchiveFormat.SevenZip;
        ////    compressor.CompressionLevel = SevenZip.CompressionLevel.Normal;
        ////    compressor.EncryptHeaders = true;
        ////    compressor.ZipEncryptionMethod = SevenZip.ZipEncryptionMethod.Aes256;

        ////    var byteArray = Encoding.UTF8.GetBytes(text);
        ////    using Stream inStream = new MemoryStream(byteArray);
        ////    using Stream outStream = new MemoryStream();

        ////    compressor.CompressStream(inStream, outStream, password);

        ////    outStream.Position = 0;
        ////    StreamReader sr = new StreamReader(outStream, Encoding.UTF8);


        ////    var extractor = new SevenZip.SevenZipExtractor(outStream, password);

        ////    var xx = new MemoryStream();
        ////    extractor.ExtractFile(0, xx);

        ////    xx.Position = 0;
        ////    var zzzzzz = new StreamReader(xx);
        ////    var bla = zzzzzz.ReadToEnd();

        ////    var r = sr.ReadToEnd();

        ////    return r;
        ////}

        ////public string DecyptString(string text, string password)
        ////{
        ////    var byteArray = Encoding.UTF8.GetBytes(text);
        ////    var z = new MemoryStream(byteArray);

        ////    z.Position = 0;
        ////    var extractor = new SevenZip.SevenZipExtractor(z, password);

        ////    Stream out2 = new MemoryStream();

        ////    extractor.ExtractFile(0, out2);

        ////    StreamReader sr = new StreamReader(out2, Encoding.UTF8);
        ////    var r = sr.ReadToEnd();

        ////    return r;
        ////}
    }
}
