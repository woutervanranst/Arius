using Arius.Core.Services;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests;

class CryptoTests : TestBase
{
    [Test]
    public async Task EncryptAndDecrypt_String_Equal()
    {
        var original = "hahahahaha";
        var passphrase = "mypassword";
        var encrypted = CryptoService.Encrypt(original, passphrase); 

        var decrypted = CryptoService.Decrypt(encrypted, passphrase);

        Assert.AreEqual(original, decrypted);
    }



    [Test]
    public async Task EncryptAndDecrypt_File_Equal()
    {
        var encFile = Path.GetTempFileName();
        var decFile = Path.GetTempFileName();

        const string passphrase = "testpw";

        try
        {
            var sourceFile = EnsureArchiveTestDirectoryFileInfo();

            using (var ss = File.OpenRead(sourceFile.FullName))
            {
                using (var es = File.OpenWrite(encFile))
                {
                    await CryptoService.CompressAndEncryptAsync(ss, es, passphrase);
                }
            }

            using (var es = File.OpenRead(encFile))
            {
                using (var ts = File.OpenWrite(decFile))
                {
                    await CryptoService.DecryptAndDecompressAsync(es, ts, passphrase);
                }
            }

            var h1 = SHA256Hasher.GetHashValue(sourceFile.FullName, string.Empty);
            var h2 = SHA256Hasher.GetHashValue(decFile, string.Empty);

            Assert.AreEqual(h1, h2);
        }
        finally
        {
            File.Delete(encFile);
            File.Delete(decFile);
        }
    }

    [Test]
    public async Task DecryptWithOpenSsl_File_Equal()
    {
        // Ensure compatibility with openssl

        var openssl = ExternalProcess.FindFullName("openssl.exe", "openssl");
        var gzip = ExternalProcess.FindFullName("gzip.exe", "gzip");

        var encFile = Path.GetTempFileName();
        var decFile = Path.GetTempFileName();

        const string passphrase = "testpw";

        try
        {
            var sourceFile = EnsureArchiveTestDirectoryFileInfo();

            using (var ss = File.OpenRead(sourceFile.FullName))
            {
                using (var es = File.OpenWrite(encFile))
                {
                    await CryptoService.CompressAndEncryptAsync(ss, es, passphrase);
                }
            }

            ExternalProcess.RunSimpleProcess(openssl, $"enc -d -aes-256-cbc -in {encFile} -out {decFile}.gz -pass pass:\"{passphrase}\" -pbkdf2");
            ExternalProcess.RunSimpleProcess(gzip, $"-d \"{decFile}.gz\" -f"); //-f for overwrite

            var h1 = SHA256Hasher.GetHashValue(sourceFile.FullName, string.Empty);
            var h2 = SHA256Hasher.GetHashValue(decFile, string.Empty);

            Assert.AreEqual(h1, h2);
        }
        finally
        {
            File.Delete(encFile);
            File.Delete(decFile);
            File.Delete($"{decFile}.gz");
        }
    }
}