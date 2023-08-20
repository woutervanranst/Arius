using System;
using Arius.Core.Services;
using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using WouterVanRanst.Utils;

namespace Arius.Core.Tests.UnitTests;

class CryptoTests : TestBase
{
    private readonly SHA256Hasher hasher;
    public CryptoTests()
    {
        hasher = new SHA256Hasher("bla");
    }

    [Test]
    public void EncryptAndDecrypt_String_Equal()
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
            TestSetup.StageArchiveTestDirectory(out FileInfo sourceFile);

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

            var h1 = await hasher.GetBinaryHashAsync(sourceFile.FullName);
            var h2 = await hasher.GetBinaryHashAsync(decFile);

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
        var openssl = ExternalProcess.FindFullName("openssl.exe", "openssl"); //add 'C:\Program Files\OpenSSL-Win64\bin' to the PATH - install https://wiki.openssl.org/index.php/Binaries
        var gzip = ExternalProcess.FindFullName("gzip.exe", "gzip"); //add 'C:\Program Files\Git\usr\bin\' to the PATH

        var encFile = Path.GetTempFileName();
        var decFile = Path.GetTempFileName();

        const string passphrase = "testpw";

        try
        {
            TestSetup.StageArchiveTestDirectory(out FileInfo sourceFile);

            using (var ss = File.OpenRead(sourceFile.FullName))
            {
                using (var es = File.OpenWrite(encFile))
                {
                    await CryptoService.CompressAndEncryptAsync(ss, es, passphrase);
                }
            }

            ExternalProcess.RunSimpleProcess(openssl, $"enc -d -aes-256-cbc -in {encFile} -out {decFile}.gz -pass pass:\"{passphrase}\" -pbkdf2");
            ExternalProcess.RunSimpleProcess(gzip, $"-d \"{decFile}.gz\" -f"); //-f for overwrite

            var h1 = await hasher.GetBinaryHashAsync(sourceFile.FullName);
            var h2 = await hasher.GetBinaryHashAsync(decFile);

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