using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Arius
{
    class SevenZipUtils
    {
        public void Encrypt(string sourceFile, string targetFile, string password)
        {
            var lib = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Environment.Is64BitProcess ? "x64" : "x86", "7z.dll");
            SevenZip.SevenZipBase.SetLibraryPath(lib);

            var compressor = new SevenZip.SevenZipCompressor();
            compressor.ArchiveFormat = SevenZip.OutArchiveFormat.SevenZip;
            compressor.CompressionLevel = SevenZip.CompressionLevel.None;
            compressor.EncryptHeaders = true;
            compressor.ZipEncryptionMethod = SevenZip.ZipEncryptionMethod.Aes256;

            compressor.CompressFilesEncrypted(targetFile, password, sourceFile);
        }
    }
}
