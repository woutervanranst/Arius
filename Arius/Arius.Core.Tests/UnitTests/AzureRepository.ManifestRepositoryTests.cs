using Arius.Core.Models;
using Arius.Core.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;

namespace Arius.Core.Tests.UnitTests;

class ManifestRepositoryTests : TestBase
{
    [Test]
    public void GetChunkHashesAsync_InvalidManifestHash_InvalidOperationException()
    {
        var manifestRepo = GetRepository();

        Assert.CatchAsync<InvalidOperationException>(async () => await manifestRepo.Binaries.GetChunkHashesAsync(new BinaryHash("idonotexist")));
    }
}