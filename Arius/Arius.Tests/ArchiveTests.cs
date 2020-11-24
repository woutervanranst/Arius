using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Reflection;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Internal;

// https://www.automatetheplanet.com/nunit-cheat-sheet/

namespace Arius.Tests
{
    public class ArchiveTests
    {
        //private AriusRepository archive;
        //private LocalRootRepository root;

        [OneTimeSetUp]
        public void ClassInit()
        {
            // Executes once for the test class. (Optional)

            //var options = GetArchiveOptions(TestSetup.accountName, TestSetup.accountKey, TestSetup.passphrase, TestSetup.container.Name, true, "fdsfsd", 0, false, TestSetup.rootDirectoryInfo.FullName);



            

            //var lff = new LocalFileFactory(new SHA256Hasher(options));

            //root = new LocalRootRepository(options, config, lff);

            //var logger = Mock.Of<ILogger<AriusRepository>>();
            //var logger2 = Mock.Of<ILogger<AzCopier>>();
            //var remoteblobfactory = new RemoteBlobFactory();

            //var uploader = new AzCopier(options, logger2, remoteblobfactory);

            //var manifestrepo = new LocalManifestFileRepository(options, config, )

            //archive = new AriusRepository(options, logger, uploader,  )
        }

        [SetUp]
        public void TestInit()
        {
            // Runs before each test. (Optional)
        }

        //[Test]
        //public void Test()
        //{
        //    //var pointerFiles = TestSetup.sourceFolder., 
        //    //    ;

        //    DirectoryExtensions.DirectoryCopy(TestSetup.sourceFolder.FullName, TestSetup.rootDirectoryInfo.FullName, true);

        //    var pointerFileInfos = TestSetup.rootDirectoryInfo.GetFiles("*" + typeof(LocalPointerFile).GetCustomAttribute<ExtensionAttribute>().Extension);
        //    var contentFileInfos = TestSetup.rootDirectoryInfo.GetFiles("*" + typeof(LocalContentFile).GetCustomAttribute<ExtensionAttribute>().Extension);

        //    IEnumerable<ILocalFile> localPointerFiles = root.GetAll<LocalPointerFile>();
        //    IEnumerable<ILocalFile> localContentFiles = root.Get<LocalContentFile>();

        //    Assert.AreEqual(pointerFileInfos.Length, localPointerFiles.Count());
        //    Assert.AreEqual(contentFileInfos.Length, localContentFiles.Count());

        //    Assert.IsTrue(localPointerFiles.All(lpf => pointerFileInfos.SingleOrDefault(fi => fi.FullName == lpf.FullName) is not null));
        //    Assert.IsTrue(localContentFiles.All(lcf => contentFileInfos.SingleOrDefault(fi => fi.FullName == lcf.FullName) is not null));

        //    var hashedAndGrouped = localPointerFiles.Union(localContentFiles).GroupBy(c => c.Hash).ToImmutableArray();
        //}

        [Test, Order(10)]
        public void ArchiveFirstFile()
        {
            //Set up the temp folder -- Copy First file to the temp folder
            var firstFile = TestSetup.sourceFolder.GetFiles().First();
            firstFile = TestSetup.CopyFile(firstFile, TestSetup.rootDirectoryInfo);

            //Execute Archive
            var services = ArchiveCommand(false, AccessTier.Cool);

            //Check outcome
            // One manifest and one binary should be uploaded
            var lmfr = services.GetRequiredService<LocalManifestFileRepository>();
            var recr = services.GetRequiredService<RemoteEncryptedChunkRepository>();

            Assert.AreEqual(1, lmfr.GetAll().Count());
            Assert.AreEqual(1, recr.GetAllChunkBlobItems().Count());

            //Get the manifest entries
            var pointerFile = GetPointerFile(services, firstFile);
            var entries = GetManifestEntries(services, pointerFile);

            //We have exactly one entry
            Assert.AreEqual(1, entries.Count());

            var firstEntry = entries.First();

            // Evaluate the the entry
            Assert.AreEqual(Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pointerFile.FullName), firstEntry.RelativeName);
            Assert.AreEqual(false, firstEntry.IsDeleted);
            Assert.AreEqual(firstFile.CreationTimeUtc, firstEntry.CreationTimeUtc);
            Assert.AreEqual(firstFile.LastWriteTimeUtc, firstEntry.LastWriteTimeUtc);
        }




        //        [Test, Order(20)]
        //        public void ArchiveSecondFileDuplicate()
        //        {
        //            //Add a duplicate of the first file
        //            var firstFile = TestSetup.sourceFolder.GetFiles().First();
        //            var secondFile = TestSetup.CopyFile(firstFile, TestSetup.rootDirectoryInfo, $"Copy of {firstFile.Name}");

        //            // Modify datetime slightly
        //            secondFile.CreationTimeUtc += TimeSpan.FromSeconds(10);
        //            secondFile.LastWriteTimeUtc += TimeSpan.FromSeconds(10);


        //            //Run archive again
        //            ArchiveCommand(false, AccessTier.Cool);


        //            //One manifest and one binary should still be there
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusManifests().Count());
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusChunks().Count());

        //            //Get the manifest entries
        //            var entries = GetManifestEntries(secondFile);

        //            //We have exactly two entries
        //            Assert.AreEqual(2, entries.Count());

        //            var relativeName = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, secondFile.FullName);
        //            var secondEntry = entries.Single(lcf => lcf.RelativeName == relativeName);

        //            // Evaluate the the entry
        //            Assert.AreEqual(false, secondEntry.IsDeleted);
        //            Assert.AreEqual(secondFile.CreationTimeUtc, secondEntry.CreationTimeUtc);
        //            Assert.AreEqual(secondFile.LastWriteTimeUtc, secondEntry.LastWriteTimeUtc);
        //        }

        //        [Test, Order(30)]
        //        public void ArchiveJustAPointer()
        //        {
        //            //Add a duplicate of the pointer
        //            var firstPointer = root.GetAriusFiles().First();
        //            var secondPointerFileInfo = TestSetup.CopyFile(firstPointer, $"Copy2 of {firstPointer.Name}");
        //            var secondPointer = AriusPointerFile.FromFile(root, secondPointerFileInfo);

        //            // Modify datetime slightly
        //            secondPointerFileInfo.CreationTimeUtc += TimeSpan.FromSeconds(10);
        //            secondPointerFileInfo.LastWriteTimeUtc += TimeSpan.FromSeconds(10);


        //            //Run archive again
        //            ArchiveCommand(false, AccessTier.Cool);


        //            //One manifest and one binary should still be there
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusManifests().Count());
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusChunks().Count());

        //            //Get the manifest entries
        //            var entries = GetManifestEntries(secondPointer);

        //            //We have exactly two entries
        //            Assert.AreEqual(3, entries.Count());

        //            //var relativeName = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, secondFile.FullName);
        //            var secondEntry = entries.Single(lcf => lcf.RelativeName == secondPointer.RelativeLocalContentFileName);

        //            // Evaluate the the entry
        //            Assert.AreEqual(false, secondEntry.IsDeleted);
        //            Assert.AreEqual(secondPointerFileInfo.CreationTimeUtc, secondEntry.CreationTimeUtc);
        //            Assert.AreEqual(secondPointerFileInfo.LastWriteTimeUtc, secondEntry.LastWriteTimeUtc);
        //        }

        //        [Test, Order(40)]
        //        public void RenameLocalContentFileWithPointer()
        //        {
        //            var file = root.GetNonAriusFiles().First();
        //            var originalFileFullName = file.FullName;
        //            TestSetup.MoveFile(file, $"Moving of {file.Name}");
        //            var pointer = new FileInfo(originalFileFullName + ".arius"); 
        //            TestSetup.MoveFile(pointer, $"Moving of {pointer.Name}");

        //            //Run archive again
        //            ArchiveCommand(false, AccessTier.Cool);

        //            //One manifest and one binary should still be there
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusManifests().Count());
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusChunks().Count());

        //            //Get the manifest entries
        //            var manifest = GetManifest(AriusPointerFile.FromFile(root, pointer));

        //            Assert.AreEqual(3+2, manifest.AriusPointerFileEntries.Count());
        //            Assert.AreEqual(3, manifest.GetLastExistingEntriesPerRelativeName().Count());

        //            var relativeNameOfOriginalFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, originalFileFullName);
        //            var relativeNameOfMovedFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, file.FullName);

        //            Assert.IsNull(manifest.GetLastExistingEntriesPerRelativeName().SingleOrDefault(lcf => lcf.RelativeName == relativeNameOfOriginalFile));
        //            var originalEntry = manifest.GetLastExistingEntriesPerRelativeName(true).Single(lcf => lcf.RelativeName == relativeNameOfOriginalFile);
        //            var movedEntry = manifest.GetLastExistingEntriesPerRelativeName().Single(lcf => lcf.RelativeName == relativeNameOfMovedFile);

        //            Assert.AreEqual(true, originalEntry.IsDeleted);
        //            Assert.AreEqual(false, movedEntry.IsDeleted);
        //        }

        //        [Test, Order(50)]
        //        public void RenameLocalContentFileWithoutPointer()
        //        {
        //            var file = root.GetNonAriusFiles().First();
        //            var originalFileFullName = file.FullName;
        //            TestSetup.MoveFile(file, $"Moving of {file.Name}");
        //            var pointer = new FileInfo(originalFileFullName + ".arius");
        //            //TestSetup.MoveFile(pointer, $"Moving of {pointer.Name}"); <---- DIT DOEN WE HIER NIET vs de vorige

        //            //Run archive again
        //            ArchiveCommand(false, AccessTier.Cool);

        //            //One manifest and one binary should still be there
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusManifests().Count());
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusChunks().Count());

        //            //Get the manifest entries
        //            var manifest = GetManifest(AriusPointerFile.FromFile(root, pointer));

        //            Assert.AreEqual(5 + 1, manifest.AriusPointerFileEntries.Count());
        //            Assert.AreEqual(4, manifest.GetLastExistingEntriesPerRelativeName().Count());

        //            var relativeNameOfOriginalFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, originalFileFullName);
        //            var relativeNameOfMovedFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, file.FullName);

        //            var originalEntry =  manifest.GetLastExistingEntriesPerRelativeName().Single(lcf => lcf.RelativeName == relativeNameOfOriginalFile);
        //            var movedEntry = manifest.GetLastExistingEntriesPerRelativeName().Single(lcf => lcf.RelativeName == relativeNameOfMovedFile);

        //            Assert.AreEqual(false, originalEntry.IsDeleted);
        //            Assert.AreEqual(false, movedEntry.IsDeleted);
        //        }

        //        [Test, Order(55)]
        //        public void TestKeepLocal()
        //        {
        //            Assert.IsTrue(root.GetNonAriusFiles().Any());

        //            ArchiveCommand(false, AccessTier.Cool, false);

        //            Assert.IsTrue(!root.GetNonAriusFiles().Any());
        //        }

        //        [Test, Order(60)]
        //        public void RenameJustPointer()
        //        {
        //            var pointerFileInfo = root.GetAriusFiles().First();
        //            var originalFileFullName = pointerFileInfo.FullName;
        //            TestSetup.MoveFile(pointerFileInfo, $"Moving123 of {pointerFileInfo.Name}");
        //            //var pointer = new FileInfo(originalFileFullName + ".arius");
        //            //TestSetup.MoveFile(pointer, $"Moving of {pointer.Name}"); <---- DIT DOEN WE HIER NIET vs de vorige

        //            //Run archive again
        //            ArchiveCommand(false, AccessTier.Cool);

        //            //One manifest and one binary should still be there
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusManifests().Count());
        //            Assert.AreEqual(1, archive.GetRemoteEncryptedAriusChunks().Count());

        //            //Get the manifest entries
        //            var manifest = GetManifest(AriusPointerFile.FromFile(root, pointerFileInfo));

        //            Assert.AreEqual(6 + 2, manifest.AriusPointerFileEntries.Count());
        //            Assert.AreEqual(4 + 0, manifest.GetLastExistingEntriesPerRelativeName().Count());

        //            var relativeNameOfOriginalFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, originalFileFullName.TrimEnd(".arius"));
        //            var relativeNameOfMovedFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pointerFileInfo.FullName.TrimEnd(".arius"));

        //            var originalEntry = manifest.GetLastExistingEntriesPerRelativeName(true).Single(lcf => lcf.RelativeName == relativeNameOfOriginalFile);
        //            var movedEntry = manifest.GetLastExistingEntriesPerRelativeName().Single(lcf => lcf.RelativeName == relativeNameOfMovedFile);

        //            Assert.AreEqual(true, originalEntry.IsDeleted);
        //            Assert.AreEqual(false, movedEntry.IsDeleted);
        //        }



        //        private FileInfo GetAriusFileInfo(string contentFile) => new FileInfo($"{contentFile}.arius");

        private ServiceProvider ArchiveCommand(bool executeAsCli, AccessTier tier, bool keepLocal = true, int minSize = 0, bool simulate = false, bool dedup = false)
        {
            if (executeAsCli)
            { 
                TestSetup.ExecuteCommandline($"archive " +
                                             $"-n {TestSetup.accountName} " +
                                             $"-k {TestSetup.accountKey} " +
                                             $"-p {TestSetup.passphrase} " +
                                             $"-c {TestSetup.container.Name} " +
                                             $"{(keepLocal ? "--keep-local" : "")} --tier hot {TestSetup.rootDirectoryInfo.FullName}");
                throw new NotImplementedException();
            }
            else
            {
                var options = GetArchiveOptions(TestSetup.accountName, TestSetup.accountKey, TestSetup.passphrase, TestSetup.container.Name,
                    keepLocal, tier.ToString(), minSize, simulate, TestSetup.rootDirectoryInfo.FullName);

                var configurationRoot = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string> { { "TempDirName", ".ariustemp" } })
                    .Build();

                var config = new Configuration(options, configurationRoot);

                var pcp = new ParsedCommandProvider{CommandExecutorOptions = options, CommandExecutorType = typeof(ArchiveCommandExecutor)};

                var services = Program.GetServiceProvider(config, pcp);

                var exec = services.GetRequiredService<ArchiveCommandExecutor>();

                exec.Execute();

                return services;
            }
                
        }

        private ArchiveOptions GetArchiveOptions(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path)
        {
            return new()
            {
                AccountName = accountName,
                AccountKey = accountKey,
                Passphrase = passphrase,
                Container = container,
                KeepLocal = keepLocal,
                Tier = tier,
                MinSize = minSize,
                Simulate = simulate,
                Path = path
            };
        }

        private IPointerFile GetPointerFile(ServiceProvider services, FileInfo localContentFileInfo)
        {
            var lrr = services.GetRequiredService<LocalRootRepository>();
            var pointerFile = lrr.GetAll().OfType<IPointerFile>().Single(pf => pf.LocalContentFileInfo.FullName == localContentFileInfo.FullName);

            return pointerFile;

        }
        private IEnumerable<Manifest.PointerFileEntry> GetManifestEntries(ServiceProvider services, IPointerFile pointerFile)
        {
            var lmfr = services.GetRequiredService<LocalManifestFileRepository>();
            var ms = services.GetRequiredService<ManifestService>();

            var mf = lmfr.GetById(pointerFile.Hash);
            
            var entries = ms.ReadManifestFile(mf).PointerFileEntries;

            return entries;
        }

        //        private IEnumerable<RemoteEncryptedAriusManifest.AriusManifest.AriusPointerFileEntry> GetManifestEntries(AriusPointerFile pointer)
        //        {
        //            var manifest = GetManifest(pointer);
        //            var entries = manifest.AriusPointerFileEntries;
        //            return entries;
        //        }

        //        private RemoteEncryptedAriusManifest.AriusManifest GetManifest(AriusPointerFile pointer)
        //        {
        //            var encrytpedManifest = archive.GetRemoteEncryptedAriusManifestByBlobItemName(pointer.EncryptedManifestName);
        //            var manifest = RemoteEncryptedAriusManifest.AriusManifest.FromRemote(encrytpedManifest, TestSetup.passphrase);
        //            return manifest;
        //        }


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




        //        /*
        //         * Delete file
        //         * Add file again that was previously deleted
        //         * Modify the binary
        //            * azcopy fails
        //         * add binary > get .arius file > delete .arius file > archive again > arius file will reappear but cannot appear twice in the manifest
        //         *
        //         *
        //         *
        //         * add binary
        //         * add another binary
        //         * add the same binary
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


              //* delete pointer, archive
        //         *
        //         */
    }
}
