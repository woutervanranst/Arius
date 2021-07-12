using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Arius.Core.Tests;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests
{
    class ByteBoundaryChunkerTests : TestBase
    {
        protected override void BeforeTestClass()
        {
            if (TestSetup.ArchiveTestDirectory.Exists) TestSetup.ArchiveTestDirectory.Delete(true);
            TestSetup.ArchiveTestDirectory.Create();
        }

        [Test]
        public void ChunkAndMerge_DedupFile_Match()
        {
            //Create the HashValueProvider and the Chunker
            var s = GetServices();
            var hvp = s.GetRequiredService<IHashValueProvider>();
            var chunker = s.GetRequiredService<ByteBoundaryChunker>();


            //Generate new dedup file
            var bf = CreateNewBinaryFile(hvp);

            //Chunk it
            var chunks = chunker.Chunk(bf);

            Assert.IsTrue(chunks.Length > 1);

            //Merge it
            var target = new FileInfo(Path.Combine(TestSetup.ArchiveTestDirectory.FullName, "dedupfile2.xyz"));
            chunker.Merge(chunks, target);

            //Calculate the hash of the result
            var hash_target = hvp.GetManifestHash(target);

            Assert.AreEqual(bf.Hash, hash_target);
        }


        private static BinaryFile CreateNewBinaryFile(IHashValueProvider hvp)
        {
            var original = Path.Combine(TestSetup.ArchiveTestDirectory.FullName, "dedupfile1.xyz");
            int sizeInKB = 1024 * 5;
            CreateRandomDedupableFile(original, sizeInKB / 10 * 1024, 10);
            var fi_original = new FileInfo(original);
            var h = hvp.GetManifestHash(fi_original);
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

        private class DummyHashValueProviderOptions : IHashValueProvider.IOptions
        {
            public string Passphrase => "test";
            public bool FastHash => false;
        }
    }
}