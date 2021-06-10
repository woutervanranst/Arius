using Arius.Core.Models;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests
{
    class AccessTierTests
    {
        [Test]
        public void StringToAccessTierOperator_AccessTier_Equals()
        {
            Assert.AreEqual((AccessTier)"hot", AccessTier.Hot); //testing for a bug that appeared in Azure.Storage.Blobs v12.8.4, this was no longer true
            Assert.AreEqual((AccessTier)"cool", AccessTier.Cool);
            Assert.AreEqual((AccessTier)"archive", AccessTier.Archive);
        }
    }
}
