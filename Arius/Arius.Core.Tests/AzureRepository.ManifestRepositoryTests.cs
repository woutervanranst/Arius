using Arius.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;

namespace Arius.Core.Tests
{
    class ManifestRepositoryTests : TestBase
    {
        [Test]
        public void GetChunkHashesAsync_InvalidManifestHash_InvalidOperationException()
        {
            var manifestRepo = GetRepository();

            Assert.CatchAsync<InvalidOperationException>(async () => await manifestRepo.GetChunkHashesForManifestAsync(new ManifestHash("idonotexist")));
        }
    }
}
