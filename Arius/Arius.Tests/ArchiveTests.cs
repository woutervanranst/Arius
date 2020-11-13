using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using NUnit.Framework;

// https://www.automatetheplanet.com/nunit-cheat-sheet/

namespace Arius.Tests
{
    public class ArchiveTests
    {
        private AriusRemoteArchive archive;
        private AriusRootDirectory root;

        [OneTimeSetUp]
        public void ClassInit()
        {
            // Executes once for the test class. (Optional)

            archive = new AriusRemoteArchive(TestSetup.accountName, TestSetup.accountKey, TestSetup.container.Name);
            root = new AriusRootDirectory(TestSetup.rootDirectoryInfo.FullName);
        }
        [SetUp]
        public void TestInit()
        {
            // Runs before each test. (Optional)
        }

        [Test, Order(10)]
        public void ArchiveFirstFile()
        {
            //Copy First file to the temp folder
            var firstFile = TestSetup.sourceFolder.GetFiles().First();
            firstFile = TestSetup.CopyFile(firstFile, TestSetup.rootDirectoryInfo);

            //Archive it
            ArchiveCommand(false, AccessTier.Cool);

            //One manifest and one binary should be uploaded
            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusManifests().Count());
            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusChunks().Count());

            //Get the manifest entries
            var entries = GetManifestEntries(firstFile);

            //We have exactly one entry
            Assert.AreEqual(1, entries.Count());

            var firstEntry = entries.First();

            // Evaluate the the entry
            Assert.AreEqual(Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, firstFile.FullName), firstEntry.RelativeName);
            Assert.AreEqual(false, firstEntry.IsDeleted);
            Assert.AreEqual(firstFile.CreationTimeUtc, firstEntry.CreationTimeUtc);
            Assert.AreEqual(firstFile.LastWriteTimeUtc, firstEntry.LastWriteTimeUtc);
        }

        


        [Test, Order(20)]
        public void ArchiveSecondFileDuplicate()
        {
            //Add a duplicate of the first file
            var firstFile = TestSetup.sourceFolder.GetFiles().First();
            var secondFile = TestSetup.CopyFile(firstFile, TestSetup.rootDirectoryInfo, $"Copy of {firstFile.Name}");

            // Modify datetime slightly
            secondFile.CreationTimeUtc += TimeSpan.FromSeconds(10);
            secondFile.LastWriteTimeUtc += TimeSpan.FromSeconds(10);


            //Run archive again
            ArchiveCommand(false, AccessTier.Cool);


            //One manifest and one binary should still be there
            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusManifests().Count());
            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusChunks().Count());

            //Get the manifest entries
            var entries = GetManifestEntries(secondFile);

            //We have exactly two entries
            Assert.AreEqual(2, entries.Count());

            var relativeName = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, secondFile.FullName);
            var secondEntry = entries.Single(lcf => lcf.RelativeName == relativeName);

            // Evaluate the the entry
            Assert.AreEqual(false, secondEntry.IsDeleted);
            Assert.AreEqual(secondFile.CreationTimeUtc, secondEntry.CreationTimeUtc);
            Assert.AreEqual(secondFile.LastWriteTimeUtc, secondEntry.LastWriteTimeUtc);



        }

        [Test]
        public void MixPointersWithLocalContent()
        {

        }

        private FileInfo GetAriusFileInfo(string contentFile) => new FileInfo($"{contentFile}.arius");

        private void ArchiveCommand(bool executeAsCli, AccessTier tier, bool keepLocal = true, int minSize = 0, bool simulate = false, bool dedup = false)
        {
            if (executeAsCli)
                TestSetup.ExecuteCommandline($"archive -n {TestSetup.accountName} -k {TestSetup.accountKey} -p {TestSetup.passphrase} -c {TestSetup.container.Name} --keep-local --tier hot {root.FullName}");
            else
                Arius.ArchiveCommand.Archive(root, archive, TestSetup.passphrase, true, AccessTier.Cool, 0, false, dedup);
        }

        private IEnumerable<RemoteEncryptedAriusManifest.AriusManifest.AriusPointerFileEntry> GetManifestEntries(FileInfo firstFile)
        {
            var pointer = AriusPointerFile.FromFile(root, GetAriusFileInfo(firstFile.FullName));
            var encrytpedManifest = archive.GetRemoteEncryptedAriusManifestByBlobItemName(pointer.EncryptedManifestName);
            var manifest = RemoteEncryptedAriusManifest.AriusManifest.FromRemote(encrytpedManifest, TestSetup.passphrase);
            var entries = manifest.AriusPointerFileEntries;
            return entries;
        }

        [TearDown]
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


        

        /*
         * Create File
         * Duplicate file
         * Rename file
         * Delete file
         * Add file again that was previously deleted
         * Rename content file
         * rename .arius file
         * Modify the binary
            * azcopy fails
         * add binary > get .arius file > delete .arius file > archive again > arius file will reappear but cannot appear twice in the manifest
         *
         *
         *
         * add binary
         * add another binary
         * add the same binary
         *
         *
            //TODO test File X is al geupload ik kopieer 'X - Copy' erbij> expectation gewoon pointer erbij binary weg
         *
         *
         * geen lingering files
         *  localcontentfile > blijft staan
         * .7z.arius weg
         *
         * dedup > chunks weg
         * .7z.arius weg
         *
         *
         * kopieer ne pointer en archiveer >> quid datetimes?
         *
         * #2
         * change a manifest without the binary present
         *
         */
    }

    [TestFixture]
    public class Test
    {

    }
}
