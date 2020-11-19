using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Microsoft.Extensions.Logging;
using SevenZip;

namespace Arius
{
    internal interface IEncrypterOptions : ICommandExecutorOptions
    {
        string Passphrase { get; }
    }

    internal class SevenZipEncrypter : IEncrypter
    {


        public SevenZipEncrypter(ICommandExecutorOptions options, 
            ILogger<SevenZipEncrypter> logger, 
            LocalFileFactory factory)
        {
            _passphrase = ((IEncrypterOptions)options).Passphrase;

            //Search async for the 7z Library (on another thread)
            _7ZLibraryPath = Task.Run(() => ExternalProcess.FindFullName(logger, "7z.dll", "7z"));

            _factory = factory;
        }

        private readonly string _passphrase;
        private readonly Task<string> _7ZLibraryPath;
        private readonly LocalFileFactory _factory;

        public IEncryptedLocalFile Encrypt(ILocalFile fileToEncrypt, string fileName)
        {
            return Encrypt(fileToEncrypt, fileName);
        }
        public IEncryptedLocalFile Encrypt(ILocalFile fileToEncrypt, string fileName, CompressionLevel compressionLevel)
        {
            throw new NotImplementedException();

            //try
            //{
            //    SevenZipBase.SetLibraryPath(_7ZLibraryPath.Result);
            //}
            //catch (SevenZipLibraryException e)
            //{
            //    throw;
            //}

            //var compressor = new SevenZipCompressor
            //{
            //    ArchiveFormat = OutArchiveFormat.SevenZip,
            //    CompressionLevel = compressionLevel,
            //    EncryptHeaders = true,
            //    ZipEncryptionMethod = ZipEncryptionMethod.Aes256
            //};

            //var archive = new FileInfo(Path.Combine(_root.Root.FullName, fileName));
            //compressor.CompressFilesEncrypted(archive.FullName, _passphrase, fileToEncrypt.FullName);

            //return (IEncrypted<V>)_factory.Create<EncryptedLocalContentFile>(_root, archive);
        }

        public IEncryptedLocalFile Encrypt(ILocalFile fileToEncrypt, string fileName, bool deletePlaintext = false)
        {
            throw new NotImplementedException();
        }



        public ILocalFile Decrypt(IEncryptedLocalFile fileToDecrypt, bool deleteEncrypted = false)
        {
            try
            {
                SevenZipBase.SetLibraryPath(_7ZLibraryPath.Result);
            }
            catch (SevenZipLibraryException e)
            {
                throw;
            }

            var decryptedType = fileToDecrypt.GetType().GetCustomAttribute<ExtensionAttribute>().DecryptedType;
            var decryptedTypeExtension = decryptedType.GetCustomAttribute<ExtensionAttribute>().Extension;

            var targetFile = new FileInfo(Path.Combine(fileToDecrypt.Root.FullName, 
                $"{fileToDecrypt.NameWithoutExtension}{decryptedTypeExtension}"));

            using (var s = targetFile.OpenWrite())
            {
                using (var extractor = new SevenZip.SevenZipExtractor(fileToDecrypt.FullName, _passphrase))
                {
                    if (extractor.ArchiveFileNames.Count > 1)
                        throw new ArgumentException("ARFCHIVE TOO MANY FILES"); //TODO
                    extractor.ExtractFile(0, s);
                }
                s.Close();
            }

            var decryptedLocalFile = _factory.Create<ILocalFile>(targetFile, fileToDecrypt.Root);

            if (deleteEncrypted)
                fileToDecrypt.Delete();

            return decryptedLocalFile;
        }
    }
}
