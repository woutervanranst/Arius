using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests
{
    class ChunkRepositoryTests : TestBase
    {
        [Test]
        public async Task GetChunkBlobByName_ExistingChunkBlob_ValidChunkBlob()
        {
            EnsureArchiveTestDirectoryFileInfo();
            await EnsureArchiveCommandHasRun();

            var repo = GetRepository();

            var cb1 = repo.GetAllChunkBlobs().First();

            var cb2 = repo.GetChunkBlobByName(Repository.ChunkFolderName, cb1.Name);

            Assert.AreEqual(cb1.FullName, cb2.FullName);
        }

        [Test]
        public void GetChunkBlobByName_NotExisting_Null()
        {
            var repo = GetRepository();

            var cb = repo.GetChunkBlobByName(Repository.ChunkFolderName, "idonotexist");

            Assert.IsNull(cb);
        }
    }
}
