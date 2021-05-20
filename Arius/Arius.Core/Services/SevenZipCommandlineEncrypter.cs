using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Services
{
    internal interface IEncrypter
    {
        void Encrypt(IFile fileToEncrypt, FileInfo encryptedFile, SevenZipCommandlineEncrypter.Compression compressionLevel, bool deletePlaintext = false);
        void Decrypt(IEncryptedFile fileToDecrypt, FileInfo decryptedFile, bool deleteEncrypted = false);
    }


    internal interface IEncrypterOptions : ICommandExecutorOptions
    {
        string Passphrase { get; }
    }

    
    internal class SevenZipCommandlineEncrypter : IEncrypter
    {
        public SevenZipCommandlineEncrypter(ICommandExecutorOptions options,
            ILogger<SevenZipCommandlineEncrypter> logger)
        {
            _passphrase = ((IEncrypterOptions)options).Passphrase;

            //Search async for the 7z Library (on another thread)
            _7ZPath = Task.Run(() => ExternalProcess.FindFullName(logger, "7z.exe", "7z"));

            _logger = logger;
        }

        private readonly string _passphrase;
        private readonly Task<string> _7ZPath;
        private readonly ILogger<SevenZipCommandlineEncrypter> _logger;

        public class Compression
        {
            private Compression(string value) { Value = value; }

            public string Value { get; set; }

            public static Compression NoCompression => new("-mx0");
            public static Compression LightCompression => new("-mx1");
        }

        public void Encrypt(IFile fileToEncrypt, FileInfo encryptedFile, Compression compressionLevel, bool deletePlaintext = false)
        {
            string rawOutput = "";

            try
            {
                _logger.LogDebug($"Encrypting {fileToEncrypt.FullName}");

                //  7z a test.7z.arius -p<pw> -mhe -mx0 -ms "<file>"
                /*
                 * a        archive
                 * -mhe     header encryption
                 * -ms      solid archive
                 * -mx0     store only/no compression
                 * -mx1     light compression
                 * -mmt     multithreaded
                 * -p       passphrase
                 */

                var arguments = $@"a ""{encryptedFile.FullName}"" -p{_passphrase} -mhe {compressionLevel.Value} -ms -mmt ""{fileToEncrypt.FullName}""";

                var regex = "Everything is Ok";

                var p = new ExternalProcess(_7ZPath.Result);

                p.Execute(arguments, regex, out rawOutput);

                if (deletePlaintext)
                    fileToEncrypt.Delete();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "ERRORTODO");
                _logger.LogDebug(rawOutput); //TODO this is a bit much

                throw;
            }
        }

        

        //public EncryptedChunkFile2 Encrypt(ChunkFile2 fileToEncrypt, bool deletePlaintext = false)
        //{
        //    _logger.LogDebug($"Encrypting {fileToEncrypt.Name}");

        //    //  7z a test.7z.arius -p<pw> -mhe -mx0 -ms "<file>"
        //    /*
        //     * a        archive
        //     * -mhe     header encryption
        //     * -ms      solid archive
        //     * -mx0     store only/no compression
        //     * -mx1     light compression
        //     * -mmt     multithreaded
        //     * -p       passphrase
        //     */

        //    var targetFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{fileToEncrypt.Hash}{EncryptedChunkFile2.Extension}"));
        //    var compressionLevel = "-mx0";

        //    string arguments = $@"a ""{targetFile.FullName}"" -p{_passphrase} -mhe {compressionLevel} -ms -mmt ""{fileToEncrypt.FileFullName}""";

        //    var regex = "Everything is Ok";

        //    var p = new ExternalProcess(_7ZPath.Result);

        //    p.Execute(arguments, regex, out string rawOutput);

        //    _logger.LogDebug(rawOutput);

        //    var encryptedLocalFile = new EncryptedChunkFile2(targetFile);

        //    if (deletePlaintext)
        //        fileToEncrypt.Delete();

        //    return encryptedLocalFile;
        //}

        public void Decrypt(IEncryptedFile fileToDecrypt, FileInfo decryptedFile, bool deleteEncrypted = false)
        {
            _logger.LogDebug($"Decrypting {fileToDecrypt.Name}");

            var p = new ExternalProcess(_7ZPath.Result);

            // Extract the archive
            /*
             * 7z e <file> -p<pw>
             * e        extract
             * -p       passphrase
             */

            //Extract the archive to a separate folder
            var randomThreadSafeDirectory = new DirectoryInfo($"{fileToDecrypt.Directory.Name}{Path.DirectorySeparatorChar}{Guid.NewGuid()}");
            
            string arguments = $@"e ""{fileToDecrypt.FullName}"" -p{_passphrase} -o""{randomThreadSafeDirectory.FullName}""";
            var regex = @"Everything is Ok";

            p.Execute(arguments, regex, out string rawOutput);

            if (randomThreadSafeDirectory.GetFiles().Length > 1)
                throw new ArgumentException($"ARFCHIVE TOO MANY FILES {rawOutput}"); //TODO

            //var decryptedFile = fileToDecrypt.GetType().GetCustomAttribute<ExtensionAttribute>()!.GetDecryptedFileInfo(fileToDecrypt);

            //Move the only file in the archive to where we expect it
            randomThreadSafeDirectory.GetFiles().Single().MoveTo(decryptedFile.FullName);
            randomThreadSafeDirectory.Delete();

            if (deleteEncrypted)
                fileToDecrypt.Delete();
        }
    }
}
