using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Arius.Tests
{
    class DedupChunkerTests
    {
        [Test]
        public void AriusDedupTest()
        {
            return;

                



            //var options = new RestoreOptions { 
            //    Passphrase = "woutervr"
            //};

            //var configurationRoot = new ConfigurationBuilder()
            //        .AddInMemoryCollection(new Dictionary<string, string> { 
            //            { "TempDirName", ".ariustemp" },
            //            { "UploadTempDirName", ".ariustempupload" }
            //        })
            //        .Build();

            //var config = new Configuration(options, configurationRoot);

            //var hvp = new SHA256Hasher(null, options);


            //var dc = new DedupChunker(null, config, hvp);

            //var file = @"C:\Users\Wouter\Documents\Test\Git-2.29.2.2-64-bit - Copy.exe";
            //var fi = new FileInfo(file);

            //var bf1 = new BinaryFile(null, fi);


            //var h1 = hvp.GetHashValue(file);

            //var cs = dc.Chunk(bf1);

            //var f2 = @"C:\Users\Wouter\Documents\Test\Git-2.29.2.2-64-bit - Copy222.exe";
            //var fi2 = new FileInfo(f2);
            //var bf2 = dc.Merge(cs, fi2);

            //var h2 = hvp.GetHashValue(f2);

            //Assert.AreEqual(h1, h2);

        }

        [Test]
        public void BareMetalTest()
        {
            return;

            var options = new RestoreOptions
            {
                Passphrase = "woutervr"
            };

            var hvp = new SHA256Hasher(null, options);




            var originalFileName = @"C:\Users\Wouter\Documents\Test\Git-2.29.2.2-64-bit - Copy.exe";

            var h1 = hvp.GetHashValue(originalFileName);

            StreamBreaker.Chunk[] chunkDefs;
            using (var hasher = SHA256.Create())
            {
                var streamBreaker = new StreamBreaker();
                using var fs1 = new FileStream(originalFileName, FileMode.Open, FileAccess.Read);
                chunkDefs = streamBreaker.GetChunks(fs1, fs1.Length, hasher).ToArray();
            }


            DirectoryInfo di = new DirectoryInfo(@"C:\Users\Wouter\Documents\Test\");
            di = di.CreateSubdirectory("chunked");
            di.Delete(true);
            di.Create();


            using var fs = new FileStream(originalFileName, FileMode.Open, FileAccess.Read)
            {
                Position = 0
            };

            for (int i = 0; i < chunkDefs.Length; i++)
            {
                var chunk = chunkDefs[i];

                byte[] buff = new byte[chunk.Length];
                fs.Read(buff, 0, (int)chunk.Length);

                using var fileStream = File.Create($@"{di.FullName}\{i}");
                fileStream.Write(buff, 0, (int)chunk.Length);
                fileStream.Close();
            }




            var zzz = new List<Stream>();
            for (int i = 0; i < chunkDefs.Length; i++)
            {
                var s = new FileStream($@"{di.FullName}\{i}", FileMode.Open, FileAccess.Read);
                zzz.Add(s);
            }

            var css = new ConcatenatedStream(zzz);


            var fi2 = @"C:\Users\Wouter\Documents\Test\Git-2.29.2.2-64-bit - Copy222.exe";
            File.Delete(fi2);

            var fff = File.Create(fi2);
            css.CopyTo(fff);
            fff.Close();

            var h2 = hvp.GetHashValue(fi2);

            Assert.AreEqual(h1, h2);
        }



        [Test]
        public void BareMetalChunkWithAriusRestore()
        {
            return;

            //var options = new RestoreOptions
            //{
            //    Passphrase = "woutervr"
            //};

            //var hvp = new SHA256Hasher(null, options);




            //var originalFileName = @"C:\Users\Wouter\Documents\Test\Git-2.29.2.2-64-bit - Copy.exe";

            //var h1 = hvp.GetHashValue(originalFileName);

            //StreamBreaker.Chunk[] chunkDefs;
            //using (var hasher = SHA256.Create())
            //{
            //    var streamBreaker = new StreamBreaker();
            //    using var fs1 = new FileStream(originalFileName, FileMode.Open, FileAccess.Read);
            //    chunkDefs = streamBreaker.GetChunks(fs1, fs1.Length, hasher).ToArray();
            //}


            //DirectoryInfo di = new DirectoryInfo(@"C:\Users\Wouter\Documents\Test\");
            //di = di.CreateSubdirectory("chunked");
            //di.Delete(true);
            //di.Create();


            //using var fs = new FileStream(originalFileName, FileMode.Open, FileAccess.Read)
            //{
            //    Position = 0
            //};

            //var chunks = new List<ChunkFile>();

            //for (int i = 0; i < chunkDefs.Length; i++)
            //{
            //    var chunk = chunkDefs[i];

            //    byte[] buff = new byte[chunk.Length];
            //    fs.Read(buff, 0, (int)chunk.Length);

            //    using var fileStream = File.Create($@"{di.FullName}\{i}");
            //    fileStream.Write(buff, 0, (int)chunk.Length);
            //    fileStream.Close();


            //    chunks.Add(new ChunkFile(new FileInfo($@"{di.FullName}\{i}"), default));
            //}






            //var configurationRoot = new ConfigurationBuilder()
            //        .AddInMemoryCollection(new Dictionary<string, string> {
            //            { "TempDirName", ".ariustemp" },
            //            { "UploadTempDirName", ".ariustempupload" }
            //        })
            //        .Build();

            //var config = new Configuration(options, configurationRoot);



            //var dc = new DedupChunker(null, config, hvp);


            //var fi2 = @"C:\Users\Wouter\Documents\Test\Git-2.29.2.2-64-bit - Copy222.exe";
            //var fil2 = new FileInfo(fi2);
            //var bf = dc.Merge(chunks.ToArray(), fil2);




            //var h2 = hvp.GetHashValue(fi2);

            //Assert.AreEqual(h1, h2);
        }


        [Test]
        public void AriusChunkWithBareMetalRestore()
        {
            return;


            //var options = new RestoreOptions
            //{
            //    Passphrase = "woutervr"
            //};

            //var configurationRoot = new ConfigurationBuilder()
            //        .AddInMemoryCollection(new Dictionary<string, string> {
            //            { "TempDirName", ".ariustemp" },
            //            { "UploadTempDirName", ".ariustempupload" }
            //        })
            //        .Build();

            //var config = new Configuration(options, configurationRoot);

            //var hvp = new SHA256Hasher(null, options);


            //var dc = new DedupChunker(null, config, hvp);

            //var file = @"C:\Users\Wouter\Documents\Test\Git-2.29.2.2-64-bit - Copy.exe";
            //var fi = new FileInfo(file);

            //var bf1 = new BinaryFile(null, fi);


            //var h1 = hvp.GetHashValue(file);

            //var chunks = dc.Chunk(bf1);






            //var zzz = new List<Stream>();
            //for (int i = 0; i < chunks.Length; i++)
            //{
            //    var s = new FileStream(chunks[i].FullName, FileMode.Open, FileAccess.Read);
            //    zzz.Add(s);
            //}

            //var css = new ConcatenatedStream(zzz);


            //var fi2 = @"C:\Users\Wouter\Documents\Test\Git-2.29.2.2-64-bit - Copy222.exe";
            //File.Delete(fi2);

            //var fff = File.Create(fi2);
            //css.CopyTo(fff);
            //fff.Close();

            //var h2 = hvp.GetHashValue(fi2);

            //Assert.AreEqual(h1, h2);

        }
    }
}
