using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Arius.Core.Tests
{
    class Archive_DirectoryTests : TestBase
    {
        protected override void BeforeEachTest()
        {
            ArchiveTestDirectory.Clear();
        }


        private readonly Lazy<FileInfo[]> sourceFiles = new(() =>
        {
            return new []
            { 
                TestSetup.CreateRandomFile(Path.Combine(SourceFolder.FullName, nameof(Archive_DirectoryTests), "file 1.txt"), 0.5),
                TestSetup.CreateRandomFile(Path.Combine(SourceFolder.FullName, nameof(Archive_DirectoryTests), "file 2.doc"), 2),
                TestSetup.CreateRandomFile(Path.Combine(SourceFolder.FullName, nameof(Archive_DirectoryTests), "file 3 large.txt"), 5),
                TestSetup.CreateRandomFile(Path.Combine(SourceFolder.FullName, nameof(Archive_DirectoryTests), "directory with spaces", "file4 with space.txt"), 1)
            };
        });
        private FileInfo[] EnsureArchiveTestDirectoryFileInfos()
        {
            var sfis = sourceFiles.Value;
            return sfis.Select(sfi => sfi.CopyTo(SourceFolder, ArchiveTestDirectory)).ToArray();
        }




        //        /*
        //         * Delete file
        //* delete pointer, archive

        //         * Add file again that was previously deleted
        //         * Modify the binary
        //            * azcopy fails
        //         * add binary > get .arius file > delete .arius file > archive again > arius file will reappear but cannot appear twice in the manifest
        //         *
        //         *
        //         *
        //         * add binary
        //         * add another binary
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
        //         *
        //         */







        public static async Task EnsureFullDirectoryArchived(bool purgeRemote = false, bool dedup = false, bool removeLocal = false)
        {
            //if (purgeRemote)
            //    await TestSetup.PurgeRemote();

            ////// Empty the test directory
            ////ArchiveTestDirectory.Clear();
            ////SourceFolder.CopyTo(ArchiveTestDirectory);
            //var bfis = ensurearc

            ////EXECUTE
            //await ArchiveCommand(AccessTier.Cool, removeLocal: removeLocal, dedup: dedup);
        }



        [Test]
        public async Task Archive_FullDirectory()
        {
            await EnsureFullDirectoryArchived();

        }
    }
}
