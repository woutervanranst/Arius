//using SevenZip;
//using System;
//using System.IO;
//using System.Linq;
//using System.Reflection;

//namespace Arius
//{
//    internal class SevenZipUtils
//    {
//        static SevenZipUtils()
//        {
//            var lib = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
//                Environment.Is64BitProcess ? "x64" : "x86", "7z.dll");
//            SevenZipBase.SetLibraryPath(lib);
//        }

//        public void DecryptFile(string sourceFile, string targetFile, string password)
//        {
//            var tfi = new FileInfo(targetFile);

//            var extractor = new SevenZip.SevenZipExtractor(sourceFile, password);

//            var di = Directory.CreateDirectory(Path.Combine(tfi.DirectoryName, Guid.NewGuid().ToString()));
//            extractor.ExtractArchive(di.FullName);

//            File.Move(Path.Combine(di.FullName, extractor.ArchiveFileNames.Single()), targetFile, true);

//            if (di.EnumerateFiles().Any())
//                throw new NotImplementedException(); // Folder should be empty but is not

//            di.Delete();
//        }

//}