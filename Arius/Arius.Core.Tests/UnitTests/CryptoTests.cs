using Arius.Core.Services;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests
{
    class CryptoTests : TestBase
    {
        [Test]
        public async Task Ha()
        {
            var original = "hahahahaha";
            var passphrase = "mypassword";
            var encrypted = CryptoService.Encrypt(original, passphrase); 

            var decrypted = CryptoService.Decrypt(encrypted, passphrase);

            Assert.AreEqual(original, decrypted);
        }
    }
}
