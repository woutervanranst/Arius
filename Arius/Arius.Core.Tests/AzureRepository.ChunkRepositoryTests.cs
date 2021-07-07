using Arius.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Linq;

namespace Arius.Core.Tests
{
    class ChunkRepositoryTests : TestBase
    {
        [Test]
        public void GetChunkBlobByName_ExistingChunkBlob_ValidChunkBlob()
        {
            var repo = GetRepository(TestSetup.ArchiveTestDirectory);
            
            var cb1 = repo.GetAllChunkBlobs().First();

            var cb2 = repo.GetChunkBlobByName(Repository.ChunkDirectoryName, cb1.Name);

            Assert.AreEqual(cb1.FullName, cb2.FullName);
        }


        public void GetChunkBlobByName_NotExisting_Null()
        {
            var repo = GetRepository(TestSetup.ArchiveTestDirectory);

            var cb = repo.GetChunkBlobByName(Repository.ChunkDirectoryName, "idonotexist");
            
            Assert.IsNull(cb);
        }
    }
}
