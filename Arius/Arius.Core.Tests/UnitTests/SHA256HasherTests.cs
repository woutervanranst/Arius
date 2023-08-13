using Azure.Storage.Blobs.Models;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.Tests.UnitTests;

internal class SHA256HasherTests
{
    [Test]
    public void HashingIsConsistent()
    {
        var f = Path.GetTempFileName();

        try
        {
            File.WriteAllText(f, "lorem ipsum dolorat");

            //var unsaltedHasher = new SHA256Hasher("bla");
            //Assert.AreEqual(unsaltedHasher.GetHashValue(f), "cf9a53ec13cadbdda07bcccfb07386906d7aabddc9be41fe4081450e889ec8a1");

            var saltedHasher = new SHA256Hasher("wouter");
            Assert.AreEqual(saltedHasher.GetHashValue(f), "4d801ce8f569d3f6c4acb2c6be4c2e0af4f8f30c102648651236cea267c723e2");

            Assert.AreEqual("wouter".CalculateSHA256Hash(), "791365155ba2b145691ca3c12c48d52f18b8a8afec7811c404d028f34a343454");
        }
        finally
        {
            File.Delete(f);
        }

    }
}
