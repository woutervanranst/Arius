using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Tests.ApiTests
{
    class Dedup_Tests : TestBase
    {
        [Test]
        public async Task Archive_OneFile_Dedup_Success()
        {
            var bfi = EnsureArchiveTestDirectoryFileInfo();
            await ArchiveCommand(dedup: true);
        }
    }
}
