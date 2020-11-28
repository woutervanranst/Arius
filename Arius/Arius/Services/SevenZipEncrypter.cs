using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Microsoft.Extensions.Logging;
//using SevenZip;

namespace Arius.Services
{
    internal interface IEncrypterOptions : ICommandExecutorOptions
    {
        string Passphrase { get; }
    }

    //internal class SevenZipEncrypter : IEncrypter
    //{
    //    public SevenZipEncrypter(ICommandExecutorOptions options,
    //        ILogger<SevenZipEncrypter> logger,
    //        LocalFileFactory factory)
    //    {
    //        _passphrase = ((IEncrypterOptions)options).Passphrase;

    //        //Search async for the 7z Library (on another thread)
    //        _7ZLibraryPath = Task.Run(() => ExternalProcess.FindFullName(logger, "7z.dll", "7z"));

    //        _factory = factory;
    //    }

    //    private readonly string _passphrase;
    //    private readonly Task<string> _7ZLibraryPath;
    //    private readonly LocalFileFactory _factory;

    //    public IEncryptedLocalFile Encrypt(ILocalFile fileToEncrypt, bool deletePlaintext = false)
    //    {
    //        try
    //        {
    //            SevenZipBase.SetLibraryPath(_7ZLibraryPath.Result);
    //        }
    //        catch (SevenZipLibraryException e)
    //        {
    //            throw;
    //        }

    //        CompressionLevel cl;
    //        if (fileToEncrypt is IChunkFile) // CHUNK?
    //            cl = CompressionLevel.None;
    //        else if (fileToEncrypt is IManifestFile)
    //            cl = CompressionLevel.Normal;
    //        else
    //            throw new NotImplementedException();

    //        var compressor = new SevenZipCompressor
    //        {
    //            ArchiveFormat = OutArchiveFormat.SevenZip,
    //            CompressionLevel = cl,
    //            EncryptHeaders = true,
    //            ZipEncryptionMethod = ZipEncryptionMethod.Aes256
    //        };

    //        //var encryptedType = fileToEncrypt.GetType().GetCustomAttribute<ExtensionAttribute>()!.EncryptedType;
    //        //var encryptedTypeExtension = encryptedType.GetCustomAttribute<ExtensionAttribute>()!.Extension;

    //        //var targetFile = new FileInfo(Path.Combine(fileToEncrypt.Root.FullName, $"{fileToEncrypt.Hash}{encryptedTypeExtension}"));

    //        var targetFile = fileToEncrypt.GetType().GetCustomAttribute<ExtensionAttribute>()!.GetEncryptedFileInfo(fileToEncrypt);

    //        compressor.CompressFilesEncrypted(targetFile.FullName, _passphrase, fileToEncrypt.FullName);

    //        //var encryptedType 
    //        //var encryptedLocalFile = (IEncryptedLocalFile)Convert.ChangeType(
    //        //    _factory.Create<IEncryptedLocalFile>(targetFile, fileToEncrypt.Root),
    //        //    encryptedType);
    //        var encryptedLocalFile = (IEncryptedLocalFile)_factory.Create(targetFile, fileToEncrypt.Root);


    //        if (deletePlaintext)
    //            fileToEncrypt.Delete();

    //        return encryptedLocalFile;
    //    }



    //    public ILocalFile Decrypt(IEncryptedLocalFile fileToDecrypt, bool deleteEncrypted = false)
    //    {
    //        try
    //        {
    //            SevenZipBase.SetLibraryPath(_7ZLibraryPath.Result);
    //        }
    //        catch (SevenZipLibraryException e)
    //        {
    //            throw;
    //        }

    //        //var decryptedType = fileToDecrypt.GetType().GetCustomAttribute<ExtensionAttribute>().DecryptedType;
    //        //var decryptedTypeExtension = decryptedType.GetCustomAttribute<ExtensionAttribute>().Extension;

    //        //var targetFile = new FileInfo(Path.Combine(fileToDecrypt.Root.FullName, 
    //        //    $"{fileToDecrypt.NameWithoutExtension}{decryptedTypeExtension}"));

    //        var targetFile = fileToDecrypt.GetType().GetCustomAttribute<ExtensionAttribute>()!.GetDecryptedFileInfo(fileToDecrypt);

    //        using (var s = targetFile.OpenWrite())
    //        {
    //            using (var extractor = new SevenZip.SevenZipExtractor(fileToDecrypt.FullName, _passphrase))
    //            {
    //                if (extractor.ArchiveFileNames.Count > 1)
    //                    throw new ArgumentException("ARFCHIVE TOO MANY FILES"); //TODO
    //                extractor.ExtractFile(0, s);
    //            }
    //            s.Close();
    //        }

    //        var decryptedLocalFile = _factory.Create(targetFile, fileToDecrypt.Root);

    //        if (deleteEncrypted)
    //            fileToDecrypt.Delete();

    //        return decryptedLocalFile;
    //    }
    //}
}
