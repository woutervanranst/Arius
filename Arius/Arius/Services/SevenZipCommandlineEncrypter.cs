using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Microsoft.Extensions.Logging;

namespace Arius.Services
{
    internal interface IEncrypterOptions : ICommandExecutorOptions
    {
        string Passphrase { get; }
    }

    internal class SevenZipCommandlineEncrypter : IEncrypter
    {
        public SevenZipCommandlineEncrypter(ICommandExecutorOptions options,
            ILogger<SevenZipCommandlineEncrypter> logger,
            LocalFileFactory factory,
            RemoteEncryptedChunkRepository chunkRepository)
        {
            _passphrase = ((IEncrypterOptions)options).Passphrase;

            //Search async for the 7z Library (on another thread)
            _7ZPath = Task.Run(() => ExternalProcess.FindFullName(logger, "7z.exe", "7z"));

            _logger = logger;
            _factory = factory;
            _chunkRepository = chunkRepository;
        }

        private readonly string _passphrase;
        private readonly Task<string> _7ZPath;
        private readonly ILogger<SevenZipCommandlineEncrypter> _logger;
        private readonly LocalFileFactory _factory;
        private readonly RemoteEncryptedChunkRepository _chunkRepository;

        public IEncryptedLocalFile Encrypt(ILocalFile fileToEncrypt, bool deletePlaintext = false)
        {
            _logger.LogDebug($"Encrypting {fileToEncrypt.Name}");

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

            string compressionLevel;
            FileInfo targetFile;
            if (fileToEncrypt is IChunkFile) // CHUNK?
            { 
                compressionLevel = "-mx0";
                targetFile = fileToEncrypt.GetType().GetCustomAttribute<ExtensionAttribute>()!.GetEncryptedFileInfo(fileToEncrypt, _chunkRepository);
            }
            else if (fileToEncrypt is IManifestFile)
            { 
                compressionLevel = "-mx1";
                targetFile = fileToEncrypt.GetType().GetCustomAttribute<ExtensionAttribute>()!.GetEncryptedFileInfo(fileToEncrypt);
            }
            else
                throw new NotImplementedException();

            string arguments;
            //if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            //    arguments = $@"a ""{targetFile.FullName}"" -p{_passphrase} -mhe {compressionLevel} -ms -mmt ""{fileToEncrypt.FullName}""";
            //else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                arguments = $@"a ""{targetFile.FullName}"" -p{_passphrase} -mhe {compressionLevel} -ms -mmt ""{fileToEncrypt.FullName}""";
            //else
            //    throw new NotImplementedException("OS Platform is not Windows or Linux");


            var regex = "Everything is Ok";

            var p = new ExternalProcess(_7ZPath.Result);

            p.Execute(arguments, regex, out string rawOutput);

            _logger.LogDebug(rawOutput);

            var encryptedLocalFile = (IEncryptedLocalFile)_factory.Create(targetFile, fileToEncrypt.Root);

            if (deletePlaintext)
                fileToEncrypt.Delete();

            return encryptedLocalFile;
        }

        public ILocalFile Decrypt(IEncryptedLocalFile fileToDecrypt, bool deleteEncrypted = false)
        {
            _logger.LogDebug($"Decrypting {fileToDecrypt.Name}");

            // Validate the archive
            string arguments;
            //if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            //    arguments = $@"l '{fileToDecrypt.FullName}' -p{_passphrase}";
            //else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                arguments = $@"l ""{fileToDecrypt.FullName}"" -p{_passphrase}";
            //else
            //    throw new NotImplementedException("OS Platform is not Windows or Linux");

            var regex = @"(?<numberOfFiles>\d*) files";

            var p = new ExternalProcess(_7ZPath.Result);
            p.Execute<int>(arguments, regex, "numberOfFiles", out string rawOutput, out int numberOfFiles);

            if (numberOfFiles > 1)
                throw new ArgumentException($"ARFCHIVE TOO MANY FILES {rawOutput}"); //TODO


            // Extract the archive
            /*
             * 7z e <file> -p<pw>
             * e        extract
             * -p       passphrase
             */

            var targetFile = fileToDecrypt.GetType().GetCustomAttribute<ExtensionAttribute>()!.GetDecryptedFileInfo(fileToDecrypt);

            //if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            //    arguments = $@"e '{fileToDecrypt.FullName}' -p{_passphrase} -o'{fileToDecrypt.DirectoryName}'";
            //else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                arguments = $@"e ""{fileToDecrypt.FullName}"" -p{_passphrase} -o""{fileToDecrypt.DirectoryName}""";
            //else
            //    throw new NotImplementedException("OS Platform is not Windows or Linux");

            regex = @"Everything is Ok";

            p.Execute(arguments, regex, out rawOutput);
            
            var decryptedLocalFile = _factory.Create(targetFile, fileToDecrypt.Root);

            if (deleteEncrypted)
                fileToDecrypt.Delete();

            return decryptedLocalFile;


        }
    }
}
