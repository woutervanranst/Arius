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

//        public void EncryptFile(string sourceFile, string targetFile, string password,
//            CompressionLevel compressionLevel = CompressionLevel.None)
//        {
//            var compressor = new SevenZipCompressor
//            {
//                ArchiveFormat = OutArchiveFormat.SevenZip,
//                CompressionLevel = compressionLevel,
//                EncryptHeaders = true,
//                ZipEncryptionMethod = ZipEncryptionMethod.Aes256
//            };

//            compressor.CompressFilesEncrypted(targetFile, password, sourceFile);
//        }


//        //namespace AriusCore
//        //    {
//        //        internal class ZipUtils
//        //        {
//        //            public ZipUtils(string passphrase)
//        //            {
//        //                _passphrase = passphrase;
//        //            }

//        //            private string _passphrase;

//        //            private string _sevenZipPath = @"C:\Program Files\7-Zip\7z.exe";

//        //            public void Compress(string sourceFile, string targetFile)
//        //            {
//        //                try
//        //                {
//        //                    using (var proc = new Process())
//        //                    {
//        //                        proc.StartInfo.FileName = _sevenZipPath;

//        //                        // -mhe=on      = HEADER ENCRYPTION
//        //                        // -mx0         = NO COMPRESSION
//        //                        proc.StartInfo.Arguments = $"a -p{_passphrase} \"{targetFile}\" \"{sourceFile}\" -mhe=on -mx0";

//        //                        proc.StartInfo.UseShellExecute = false;
//        //                        proc.StartInfo.RedirectStandardOutput = true;
//        //                        proc.StartInfo.RedirectStandardError = true;

//        //                        bool hasError = false;
//        //                        string errorMsg = string.Empty;

//        //                        proc.OutputDataReceived += (sender, data) => System.Diagnostics.Debug.WriteLine(data.Data);
//        //                        proc.ErrorDataReceived += (sender, data) =>
//        //                        {
//        //                            if (data.Data == null)
//        //                                return;

//        //                            System.Diagnostics.Debug.WriteLine(data.Data);

//        //                            hasError = true;
//        //                            errorMsg += data.Data;
//        //                        };

//        //                        proc.Start();
//        //                        proc.BeginOutputReadLine();
//        //                        proc.BeginErrorReadLine();

//        //                        proc.WaitForExit();

//        //                        if (proc.ExitCode != 0 || hasError)
//        //                        {
//        //                            //7z output codes https://superuser.com/questions/519114/how-to-write-error-status-for-command-line-7-zip-in-variable-or-instead-in-te

//        //                            if (File.Exists(targetFile))
//        //                                File.Delete(targetFile);

//        //                            throw new ApplicationException($"Error while compressing :  {errorMsg}");
//        //                        }
//        //                    }
//        //                }
//        //                catch (Win32Exception e) when (e.Message == "The system cannot find the file specified.")
//        //                {
//        //                    //7zip not installed
//        //                    throw new ApplicationException("7Zip CLI Not Installed", e);
//        //                }
//        //            }
//        //        }
//        //    }

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

//        ////public string EncryptString(string text, string password)
//        ////{
//        ////    var compressor = new SevenZip.SevenZipCompressor();
//        ////    compressor.ArchiveFormat = SevenZip.OutArchiveFormat.SevenZip;
//        ////    compressor.CompressionLevel = SevenZip.CompressionLevel.Normal;
//        ////    compressor.EncryptHeaders = true;
//        ////    compressor.ZipEncryptionMethod = SevenZip.ZipEncryptionMethod.Aes256;

//        ////    var byteArray = Encoding.UTF8.GetBytes(text);
//        ////    using Stream inStream = new MemoryStream(byteArray);
//        ////    using Stream outStream = new MemoryStream();

//        ////    compressor.CompressStream(inStream, outStream, password);

//        ////    outStream.Position = 0;
//        ////    StreamReader sr = new StreamReader(outStream, Encoding.UTF8);


//        ////    var extractor = new SevenZip.SevenZipExtractor(outStream, password);

//        ////    var xx = new MemoryStream();
//        ////    extractor.ExtractFile(0, xx);

//        ////    xx.Position = 0;
//        ////    var zzzzzz = new StreamReader(xx);
//        ////    var bla = zzzzzz.ReadToEnd();

//        ////    var r = sr.ReadToEnd();

//        ////    return r;
//        ////}

//        ////public string DecyptString(string text, string password)
//        ////{
//        ////    var byteArray = Encoding.UTF8.GetBytes(text);
//        ////    var z = new MemoryStream(byteArray);

//        ////    z.Position = 0;
//        ////    var extractor = new SevenZip.SevenZipExtractor(z, password);

//        ////    Stream out2 = new MemoryStream();

//        ////    extractor.ExtractFile(0, out2);

//        ////    StreamReader sr = new StreamReader(out2, Encoding.UTF8);
//        ////    var r = sr.ReadToEnd();

//        ////    return r;
//        ////}
//    }
//}