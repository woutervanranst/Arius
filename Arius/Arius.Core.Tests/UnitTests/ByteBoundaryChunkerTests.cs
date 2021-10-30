using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Arius.Core.Tests;
using Arius.Core.Tests.Extensions;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests;

class ByteBoundaryChunkerTests : TestBase
{
    protected override void BeforeTestClass()
    {
        ArchiveTestDirectory.Clear();
    }


    [Test]
    public void ChunkAndMerge_DedupFile_Match()
    {
        //Create the HashValueProvider and the Chunker
        var services = GetServices();
        var hvp = services.GetRequiredService<IHashValueProvider>();
        var logger = services.GetRequiredService<ILogger<ByteBoundaryChunker>>();

        // SCENARIO 1: the buffer is larger than the stream
        // Chunk a small stream with no minimum chunk size
        var chunker = new ByteBoundaryChunker(logger, hvp, minChunkSize: 0);
        var smallByteArray = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 0, 0, 1, 2, 3, 0, 0, 5, 6, 7, 0, 0, 1, 2, 3 };
        //var smallByteArray = new byte[] { 1, 2, 3, 0, 4, 5, 6, 7, 8, 0, 0, 1, 2, 3 };
        var smallStream = new MemoryStream(smallByteArray);
        var chunks = chunker.Chunk(smallStream).ToArray();
            
        Assert.IsTrue(chunks.Length == 4);
        Assert.AreEqual(smallByteArray, chunks.SelectMany(b => b.Bytes).ToArray());


        // SCENARIO 2: the buffer is smaller than the stream
        // Chunk a file
        chunker = new ByteBoundaryChunker(logger, hvp);

        //Generate new dedup file
        var bf = CreateNewBinaryFile(hvp);

        //Chunk it
        using (var bfs = File.OpenRead(bf.FullName))
        {
            chunks = chunker.Chunk(bfs).ToArray();

            // the chunker actually chunked something
            Assert.IsTrue(chunks.Length > 1);


            var original = File.ReadAllBytes(bf.FullName); 
            var recomposed = chunks.SelectMany(b => b.Bytes).ToArray();

            // the length of the stream is equal to the length of the sum of the chunks
            Assert.AreEqual(original.Length, recomposed.Length);

            //// the streams are equal byte for byte
            //Assert.AreEqual(original, recomposed);

            // the hashes are equal
            var originalHash = hvp.GetChunkHash(original);
            var recomposedHash = hvp.GetChunkHash(recomposed);
            Assert.AreEqual(originalHash, recomposedHash);

            foreach (var chunk in chunks)
            {
                var chunkBytes = chunk.Bytes;
                Assert.IsTrue(chunkBytes.Length >= chunker.MinChunkSize);

                if (chunk != chunks.Last())
                {
                    // each chunk (apart from the last one) ends with the delimiter
                    Assert.IsTrue(chunkBytes[(chunkBytes.Length - 2)..].SequenceEqual(chunker.Delimiter));

                    // the delimiter is the only one in this chunk
                    var indexOfDelimiter = new ReadOnlySpan<byte>(chunkBytes).IndexOf(chunker.Delimiter);
                    Assert.AreEqual(chunkBytes.Length - 2, indexOfDelimiter);
                }
                else
                {
                    //the last chunk does not contain the delimiter
                    var indexOfDelimiter = new ReadOnlySpan<byte>(chunkBytes).IndexOf(chunker.Delimiter);
                    Assert.AreEqual(-1, indexOfDelimiter);
                }
            }
        }

        //Merge it
        //var target = new FileInfo(Path.Combine(TestSetup.ArchiveTestDirectory.FullName, "dedupfile2.xyz"));
            
        //chunker.Merge(chunks, target);

        ////Calculate the hash of the result
        //var hash_target = hvp.GetManifestHash(target);

        //Assert.AreEqual(bf.Hash, hash_target);
    }


    private static BinaryFile CreateNewBinaryFile(IHashValueProvider hvp)
    {
        var original = Path.Combine(TestSetup.ArchiveTestDirectory.FullName, "dedupfile1.xyz");
        int sizeInKB = 1024 * 5;
        CreateRandomDedupableFile(original, sizeInKB / 10 * 1024, 10);
        var fi_original = new FileInfo(original);
        var h = hvp.GetBinaryHash(fi_original);
        var bf = new BinaryFile(fi_original.Directory, fi_original, h);
        return bf;
    }

    private static void CreateRandomDedupableFile(string fileFullName, long blockSizeInBytes, int repeats)
    {
        var f = new FileInfo(fileFullName);
        if (!f.Directory.Exists)
            f.Directory.Create();

        //Generate block
        byte[] block = new byte[blockSizeInBytes];
        var rng = new Random();
        rng.NextBytes(block);

        using FileStream stream = File.OpenWrite(fileFullName);
        for (int i = 0; i < repeats; i++)
            stream.Write(block, 0, block.Length);

        stream.Close();
    }
}