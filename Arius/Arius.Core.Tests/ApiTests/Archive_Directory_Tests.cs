using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Tests;
using Arius.Core.Tests.Extensions;
using Azure.Storage.Blobs.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Arius.Core.Tests.ApiTests;

class Archive_Directory_Tests : TestBase
{
    [Test]
    public async Task Archive_FullDirectory()
    {
        if (DateTime.Now <= TestSetup.UnitTestGracePeriod)
            return;

        TestSetup.StageArchiveTestDirectory(out FileInfo[] bfis);
        await ArchiveCommand();

        RepoStats(out var repo, out var chunkBlobItemCount1, out var binaryCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out _);
        //Chunks for each file
        Assert.AreEqual(bfis.Length, chunkBlobItemCount1);
        //Manifests for each file
        Assert.AreEqual(bfis.Length, binaryCount1);
        //PointerFileEntries for each file
        Assert.AreEqual(bfis.Length, currentPfeWithoutDeleted1.Length);
    }






    //        /*
    //         * Delete file
    //* delete pointer, archive

    //         * Modify the binary
    //            * azcopy fails
    //         * add binary > get .arius file > delete .arius file > archive again > arius file will reappear but cannot appear twice in the manifest
    //         *
    //         *
    //            //TODO test File X is al geupload ik kopieer 'X - Copy' erbij> expectation gewoon pointer erbij binary weg
    //         *
    //         *
    //         * geen lingering files
    //         *  localcontentfile > blijft staan
    //         * .7z.arius weg
    //         *
    //         * dedup > chunks weg
    //         * .7z.arius weg
    //         *
    //         *
    //         * kopieer ne pointer en archiveer >> quid datetimes?
    //         *
    //         * #2
    //         * change a manifest without the binary present

    // * archive a file for which ONLY the chunk (not deduped) exists (ie no pointer, no entries no manifest)
    // * archive a duplicated chunkfile
    // * chunk1, 2, 3 are already uploaded. file 2 = chunk 2,3. archive.
    //         */




}