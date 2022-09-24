using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Arius.Core.Tests;
using Arius.Core.Tests.Extensions;
using Azure.Storage.Blobs.Models;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests.ApiTests;

class Restore_Tests : TestBase
{
    protected override void BeforeEachTest()
    {
        RestoreTestDirectory.Clear();
    }






    



    [Test]
    public async Task Restore_OneFileWithArchivedChunk_CannotYetBeRestored()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        // Reset params
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal = false;
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier = false;
        //Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration = false;


        // Ensure stuff is archived
        TestSetup.StageArchiveTestDirectory(out FileInfo[] _);
        await ArchiveCommand(purgeRemote: true, removeLocal: false, tier: AccessTier.Archive);


        // Copy the pointer to the restore directory
        var a_pfi = ArchiveTestDirectory.GetPointerFileInfos().First();
        var r_pfi = a_pfi.CopyTo(RestoreTestDirectory);


        // Restore
        await RestoreCommand(RestoreTestDirectory.FullName, synchronize: false, download: true);


        // Assert
        // for the restore operation we only started a hydration
        //Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromLocal);
        //Assert.IsFalse(Commands.Restore.DownloadChunksForBinaryBlock.ChunkRestoredFromOnlineTier);
        //Assert.IsTrue(Commands.Restore.DownloadChunksForBinaryBlock.ChunkStartedHydration);

        // the BinaryFile is NOT restored
        var ps = GetPointerService();
        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
        Assert.IsNull(r_bfi);
    }

    [Test]
    public async Task Restore_OneFileWithTEMPHYDRATEDChunk_BinaryFileRestoredTempDeleted()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;
    }

    
}