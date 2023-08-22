using System;
using System.Threading.Tasks;
using Arius.Core.Models;
using FluentAssertions;
using NUnit.Framework;

namespace Arius.Core.Tests.UnitTests;

class ChunkEntriesTests : TestBase
{
    [Test]
    public async Task ChunkExists_NotExisting_False()
    {
        var ch = new ChunkHash("idonotexist".StringToBytes());

        (await Repository.ChunkExistsAsync(ch)).Should().BeFalse();
    }

    [Test]
    public async Task GetChunkEntryAsync_NotExisting_InvalidOperationException()
    {
        var ch = new ChunkHash("idonotexist".StringToBytes());

        Assert.ThrowsAsync<InvalidOperationException>(async () => await Repository.GetChunkEntryAsync(ch));
    }
}