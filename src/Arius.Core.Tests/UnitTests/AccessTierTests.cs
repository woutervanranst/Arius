﻿using Azure.Storage.Blobs.Models;
using NUnit.Framework;

namespace Arius.Core.Tests.UnitTests;

class AccessTierTests
{
    [Test]
    public void StringToAccessTierOperator_AccessTier_Equals()
    {
        Assert.AreEqual((AccessTier)"hot", AccessTier.Hot); //testing for a bug that appeared in Azure.Storage.Blobs v12.8.4, this was no longer true
        Assert.AreEqual((AccessTier)"cool", AccessTier.Cool);
        Assert.AreEqual((AccessTier)"cold", AccessTier.Cold);
        Assert.AreEqual((AccessTier)"archive", AccessTier.Archive);
    }
}