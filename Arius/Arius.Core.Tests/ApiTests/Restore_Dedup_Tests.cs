using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Tests.Extensions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Tests.ApiTests;

class Restore_Dedup_Tests : TestBase
{
    [Test]
    public async Task Restore_DedupedFile_Success()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        TestSetup.StageArchiveTestDirectory(out FileInfo _);
        await ArchiveCommand(dedup: true);

        await RestoreCommand(RestoreTestDirectory.FullName, true, true, true);

        // the BinaryFile is restored
        var ps = GetPointerService();
        var r_pfi = RestoreTestDirectory.GetPointerFileInfos().Single();
        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
        Assert.IsNotNull(r_bfi);
    }

    [Test]
    public async Task Restore_DedupedDirectory_Success()
    {
        TestSetup.StageArchiveTestDirectory(out FileInfo[] _);
        await ArchiveCommand(purgeRemote: true, dedup: true, removeLocal: false);

        // the restore directory is empty
        Assert.IsFalse(RestoreTestDirectory.EnumerateFiles().Any());

        await RestoreCommand(RestoreTestDirectory.FullName, true, true);

        Assert.IsTrue(RestoreTestDirectory.EnumerateFiles().Any());

        // TODO add actual tests
        throw new NotImplementedException();
    }
}