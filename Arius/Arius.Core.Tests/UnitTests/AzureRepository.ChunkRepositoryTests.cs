using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests
{
    class ChunkRepositoryTests : TestBase
    {
        [Test]
        public void GetChunkBlobByName_ExistingChunkBlob_ValidChunkBlob()
        {
            var repo = GetRepository();

            var cb1 = repo.GetAllChunkBlobs().First();

            var cb2 = repo.GetChunkBlobByName(Repository.ChunkDirectoryName, cb1.Name);

            Assert.AreEqual(cb1.FullName, cb2.FullName);
        }

        [Test]
        public void GetChunkBlobByName_NotExisting_Null()
        {
            var repo = GetRepository();

            var cb = repo.GetChunkBlobByName(Repository.ChunkDirectoryName, "idonotexist");

            Assert.IsNull(cb);
        }

        [Test]
        public async Task EncryptAndDecrypt_File_Equal()
        {
            var encFile = Path.GetTempFileName();
            var decFile = Path.GetTempFileName();

            try
            {
                var sourceFile = EnsureArchiveTestDirectoryFileInfo();

                using (var ss = File.OpenRead(sourceFile.FullName))
                {
                    using (var es = File.OpenWrite(encFile))
                    {
                        await Repository.CompressAndEncrypt(ss, es, "testpw");
                    }
                }

                using (var es = File.OpenRead(encFile))
                {
                    using (var ts = File.OpenWrite(decFile))
                    {
                        await Repository.DecryptAndDecompress(es, ts, "testpw");
                    }
                }

                var h1 = SHA256Hasher.GetHashValue(sourceFile.FullName, string.Empty);
                var h2 = SHA256Hasher.GetHashValue(decFile, string.Empty);

                Assert.AreEqual(h1, h2);
            }
            finally
            {
                File.Delete(encFile);
                File.Delete(decFile);
            }
        }
    }
}
