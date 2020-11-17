using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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

    internal class SevenZipEncrypter<T> : IEncrypter<T> where T : IFile, IHashable
    {


        public SevenZipEncrypter(ICommandExecutorOptions options, ILogger<SevenZipEncrypter<T>> logger, LocalRootDirectory root, LocalFileFactory factory)
        {
            _passphrase = ((IEncrypterOptions) options).Passphrase;
            
            //Search async for the 7z Library (on another thread)
            _7ZLibraryPath = Task.Run(() => ExternalProcess.FindFullName(logger, "7z.dll", "7z"));

            _root = root;

            _factory = factory;
        }

        private readonly string _passphrase;
        private readonly Task<string> _7ZLibraryPath;
        private readonly LocalRootDirectory _root;
        private readonly LocalFileFactory _factory;

        public IEncrypted<T> Encrypt(T fileToEncrypt)
        {
            return Encrypt(fileToEncrypt, CompressionLevel.None);
        }

        public IEncrypted<T> Encrypt(T fileToEncrypt, CompressionLevel compressionLevel)
        {
            try
            {
                SevenZipBase.SetLibraryPath(_7ZLibraryPath.Result);
            }
            catch (SevenZipLibraryException e)
            {
                throw;
            }
            
            var compressor = new SevenZipCompressor
            {
                ArchiveFormat = OutArchiveFormat.SevenZip,
                CompressionLevel = compressionLevel,
                EncryptHeaders = true,
                ZipEncryptionMethod = ZipEncryptionMethod.Aes256
            };

            var archive = new FileInfo(Path.Combine(_root.Root.FullName, fileToEncrypt.Hash + ".7z.arius"));
            compressor.CompressFilesEncrypted(archive.FullName, _passphrase, fileToEncrypt.FullName);

            return (IEncrypted<T>)_factory.Create<EncryptedLocalContentFile>(_root, archive);
        }

        

        public T Decrypt(IEncrypted<T> fileToDecrypt)
        {
            throw new NotImplementedException();
        }
    }
}
