using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Arius.Core.Tests
{
    class ByteBoundaryChunkerTests
    {
        [OneTimeSetUp]
        public void ClassInit()
        {
            // Executes once for the test class. (Optional)

            if (TestSetup.archiveTestDirectory.Exists) TestSetup.archiveTestDirectory.Delete(true);
            TestSetup.archiveTestDirectory.Create();
        }

        [SetUp]
        public void TestInit()
        {
            // Runs before each test. (Optional)
        }

        [Test]
        public void ChunkAndMerge_DedupFile_Match()
        {
            //Create the HashValueProvider and the Chunker
            GetServices(out var hvp, out var chunker);

            //Generate new dedup file
            var bf = CreateNewBinaryFile();
            bf.Hash = hvp.GetHashValue(bf.FullName);

            //Chunk it
            var chunks = chunker.Chunk(bf);

            Assert.IsTrue(chunks.Length > 1);

            //Merge it
            var target = new FileInfo(Path.Combine(TestSetup.archiveTestDirectory.FullName, "dedupfile2.xyz"));
            chunker.Merge(chunks, target);

            //Calculate the hash of the result
            var hash_target = hvp.GetHashValue(target.FullName);

            Assert.AreEqual(bf.Hash, hash_target);
        }


        private static void GetServices(out IHashValueProvider hvp, out ByteBoundaryChunker chunker)
        {
            //var services = await ArchiveRestoreTests.ArchiveCommand(AccessTier.Cool, dedup: true);
            var loggerFactory = (ILoggerFactory)NullLoggerFactory.Instance;
            var config = new TempDirectoryAppSettings { TempDirectoryName = "test" };
            hvp = new SHA256Hasher(loggerFactory.CreateLogger<SHA256Hasher>(), new DummyHashValueProviderOptions());
            chunker = new ByteBoundaryChunker(loggerFactory.CreateLogger<ByteBoundaryChunker>(), config, hvp);
        }

        private static BinaryFile CreateNewBinaryFile()
        {
            var original = Path.Combine(TestSetup.archiveTestDirectory.FullName, "dedupfile1.xyz");
            int sizeInKB = 1024 * 5;
            CreateRandomDedupableFile(original, sizeInKB / 10 * 1024, 10);
            var fi_original = new FileInfo(original);
            var bf = new BinaryFile(fi_original.Directory, fi_original);
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

        public void TestCleanup()
        {
            // Runs after each test. (Optional)
        }
        [OneTimeTearDown]
        public void ClassCleanup()
        {
            // Runs once after all tests in this class are executed. (Optional)
            // Not guaranteed that it executes instantly after all tests from the class.
        }
    }
}