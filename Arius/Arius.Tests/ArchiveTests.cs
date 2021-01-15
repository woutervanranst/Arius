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
using System.Threading.Tasks;
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


        [Test, Order(10)]
        public async Task ArchiveFirstFile()
        {
            //Set up the temp folder -- Copy First file to the temp folder
            var firstFile = TestSetup.sourceFolder.GetFiles().First();
            firstFile = TestSetup.CopyFile(firstFile, TestSetup.rootDirectoryInfo);

            //Execute Archive
            var services = ArchiveCommand(false, AccessTier.Cool, dedup: false);

            //Check outcome
            var repo = services.GetRequiredService<AzureRepository>();

            //One binary should be uploaded
            //Assert.AreEqual(1, (await repo.GetCurrentEntriesAsync(true)).Count());
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //Get the manifest entries
            var pointerFile = GetPointerFileOfLocalContentFile(services, firstFile);
            var entries = await repo.GetCurrentEntriesAsync(true);
            //var entries = await GetManifestEntries(repo, pointerFile, PointerFileEntryFilter.LastWithDeleted);

            //We have exactly one entry
            Assert.AreEqual(1, entries.Count());

            var firstEntry = entries.First();

            // Evaluate the the entry
            Assert.AreEqual(Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pointerFile.FullName), firstEntry.RelativeName);
            Assert.AreEqual(false, firstEntry.IsDeleted);
            Assert.AreEqual(firstFile.CreationTimeUtc, firstEntry.CreationTimeUtc);
            Assert.AreEqual(firstFile.LastWriteTimeUtc, firstEntry.LastWriteTimeUtc);
        }

        [Test, Order(20)]
        public async Task ArchiveSecondFileDuplicate()
        {
            //Modify temp folder
            //Add a duplicate of the first file
            var firstFile = TestSetup.rootDirectoryInfo.GetLocalContentFiles().First();
            var secondFile = TestSetup.CopyFile(firstFile, TestSetup.rootDirectoryInfo, $"Copy of {firstFile.Name}");

            // Modify datetime slightly
            secondFile.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            secondFile.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux


            //Execute Archive
            var services = ArchiveCommand(false, AccessTier.Cool);


            //Check outcome
            var repo = services.GetRequiredService<AzureRepository>();

            //One manifest and one binary should still be there
            //Assert.AreEqual(2, (await repo.GetCurrentEntriesAsync(true)).Count());
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //Get the manifest entries
            var pointerSecondFile = GetPointerFileOfLocalContentFile(services, secondFile);
            var entries = await repo.GetCurrentEntriesAsync(true);

            //We have exactly two entries
            Assert.AreEqual(2, entries.Count());

            var relativeName = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pointerSecondFile.FullName);
            var secondEntry = entries.Single(pfe => pfe.RelativeName == relativeName);

            // Evaluate the the entry
            Assert.AreEqual(false, secondEntry.IsDeleted);
            Assert.AreEqual(secondFile.CreationTimeUtc, secondEntry.CreationTimeUtc);
            Assert.AreEqual(secondFile.LastWriteTimeUtc, secondEntry.LastWriteTimeUtc);
        }

        [Test, Order(30)]
        public void ArchiveJustAPointer()
        {
            //Modify temp folder
            //Add a duplicate of the pointer
            var firstPointer = TestSetup.rootDirectoryInfo.GetPointerFiles().First();
            var secondPointerFileInfo = TestSetup.CopyFile(firstPointer, $"Copy2 of {firstPointer.Name}");

            // Modify datetime slightly
            secondPointerFileInfo.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            secondPointerFileInfo.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux


            //Execute Archive
            var services = ArchiveCommand(false, AccessTier.Cool);


            //Check outcome
            var repo = services.GetRequiredService<AzureRepository>();

            //One manifest and one binary should still be there
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //Get the manifest entries
            var entries = repo.GetCurrentEntries(true);

            //We have exactly three entries
            Assert.AreEqual(3, entries.Count());

            var relativeName = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, secondPointerFileInfo.FullName);
            var secondEntry = entries.Single(pfe => pfe.RelativeName == relativeName);

            // Evaluate the the entry
            Assert.AreEqual(false, secondEntry.IsDeleted);
            Assert.AreEqual(secondPointerFileInfo.CreationTimeUtc, secondEntry.CreationTimeUtc);
            Assert.AreEqual(secondPointerFileInfo.LastWriteTimeUtc, secondEntry.LastWriteTimeUtc);
        }

        //[Test, Order(40)]
        //public void RenameLocalContentFileWithPointer()
        //{
        //    //Modify temp folder
        //        //Rename a file
        //    var localContentFileFileInfo = TestSetup.rootDirectoryInfo.GetLocalContentFiles().First();
        //    var pointerFileInfo = localContentFileFileInfo.GetPointerFileInfo(); // new FileInfo(originalFileFullName + ".pointer.arius");
        //    var originalPointerFileInfoFullName = pointerFileInfo.FullName;

        //    TestSetup.MoveFile(localContentFileFileInfo, $"Moving of {localContentFileFileInfo.Name}");
        //    TestSetup.MoveFile(pointerFileInfo, $"Moving of {pointerFileInfo.Name}");


        //    //Execute Archive
        //    var services = ArchiveCommand(false, AccessTier.Cool);


        //    //Check outcome
        //    var lmfr = services.GetRequiredService<LocalManifestFileRepository>();
        //    var recr = services.GetRequiredService<RemoteEncryptedChunkRepository>();

        //        //One manifest and one binary should still be there
        //    Assert.AreEqual(1, lmfr.GetAll().Count());
        //    Assert.AreEqual(1, recr.GetAllChunkBlobItems().Count());

        //        //Get the manifest entries
        //    var pf = GetPointerFile(services, pointerFileInfo);
        //    var all = GetManifestEntries(services, pf, PointerFileEntryFilter.All);
        //    var lastExisting = GetManifestEntries(services, pf, PointerFileEntryFilter.LastExisting);
        //    var lastWithDeleted = GetManifestEntries(services, pf, PointerFileEntryFilter.LastWithDeleted);

        //    Assert.AreEqual(3 + 2, all.Count());
        //    Assert.AreEqual(3, lastExisting.Count());

        //    var relativeNameOfOriginalPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, originalPointerFileInfoFullName);
        //    var relativeNameOfMovedPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pointerFileInfo.FullName);

        //    Assert.IsNull(lastExisting.SingleOrDefault(lcf => lcf.RelativeName == relativeNameOfOriginalPointerFile));
        //    var originalEntry = lastWithDeleted.Single(lcf => lcf.RelativeName == relativeNameOfOriginalPointerFile);
        //    var movedEntry = lastExisting.Single(lcf => lcf.RelativeName == relativeNameOfMovedPointerFile);

        //    Assert.AreEqual(true, originalEntry.IsDeleted);
        //    Assert.AreEqual(false, movedEntry.IsDeleted);
        //}

        //[Test, Order(50)]
        //public void RenameLocalContentFileWithoutPointer()
        //{
        //    //Modify temp folder
        //        //Rename a file
        //    var localContentFileFileInfo = TestSetup.rootDirectoryInfo.GetLocalContentFiles().First();
        //    var pointerFileInfo = localContentFileFileInfo.GetPointerFileInfo(); // new FileInfo(originalFileFullName + ".pointer.arius");
        //    var originalPointerFileInfoFullName = pointerFileInfo.FullName;

        //    TestSetup.MoveFile(localContentFileFileInfo, $"Moving of {localContentFileFileInfo.Name}");
        //    //TestSetup.MoveFile(pointerFileInfo, $"Moving of {pointerFileInfo.Name}"); <-- Dit doen we hier NIET vs de vorige


        //    //Execute Archive
        //    var services = ArchiveCommand(false, AccessTier.Cool);


        //    //Check outcome
        //    var lmfr = services.GetRequiredService<LocalManifestFileRepository>();
        //    var recr = services.GetRequiredService<RemoteEncryptedChunkRepository>();

        //        //One manifest and one binary should still be there
        //    Assert.AreEqual(1, lmfr.GetAll().Count());
        //    Assert.AreEqual(1, recr.GetAllChunkBlobItems().Count());

        //        //Get the manifest entries
        //    var pf = GetPointerFile(services, pointerFileInfo);
        //    var all = GetManifestEntries(services, pf, PointerFileEntryFilter.All);
        //    var lastExisting = GetManifestEntries(services, pf, PointerFileEntryFilter.LastExisting);
        //    var lastWithDeleted = GetManifestEntries(services, pf, PointerFileEntryFilter.LastWithDeleted);

        //    Assert.AreEqual(5 + 1, all.Count());
        //    Assert.AreEqual(4, lastExisting.Count());

        //    var relativeNameOfOriginalPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, originalPointerFileInfoFullName);
        //    var relativeNameOfMovedPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pointerFileInfo.FullName);

        //    var originalEntry = lastWithDeleted.Single(lcf => lcf.RelativeName == relativeNameOfOriginalPointerFile);
        //    var movedEntry = lastExisting.Single(lcf => lcf.RelativeName == relativeNameOfMovedPointerFile);

        //    Assert.AreEqual(false, originalEntry.IsDeleted);
        //    Assert.AreEqual(false, movedEntry.IsDeleted);
        //}

        //[Test, Order(55)]
        //public void TestKeepLocal()
        //{
        //    Assert.IsTrue(TestSetup.rootDirectoryInfo.GetLocalContentFiles().Any());

        //    ArchiveCommand(false, AccessTier.Cool, false);

        //    Assert.IsTrue(!TestSetup.rootDirectoryInfo.GetLocalContentFiles().Any());
        //}

        //[Test, Order(60)]
        //public void RenameJustPointer()
        //{
        //    //Modify temp folder
        //        //Rename a file
        //    var pointerFileInfo = TestSetup.rootDirectoryInfo.GetPointerFiles().First();
        //    var originalPointerFileInfoFullName = pointerFileInfo.FullName;

        //    //TestSetup.MoveFile(localContentFileFileInfo, $"Moving of {localContentFileFileInfo.Name}");
        //    TestSetup.MoveFile(pointerFileInfo, $"Moving of {pointerFileInfo.Name}"); //< --Dit doen we hier NIET vs de vorige


        //    //Execute Archive
        //    var services = ArchiveCommand(false, AccessTier.Cool);


        //    //Check outcome
        //    var lmfr = services.GetRequiredService<LocalManifestFileRepository>();
        //    var recr = services.GetRequiredService<RemoteEncryptedChunkRepository>();

        //        //One manifest and one binary should still be there
        //    Assert.AreEqual(1, lmfr.GetAll().Count());
        //    Assert.AreEqual(1, recr.GetAllChunkBlobItems().Count());

        //        //Get the manifest entries
        //    var pf = GetPointerFile(services, pointerFileInfo);
        //    var all = GetManifestEntries(services, pf, PointerFileEntryFilter.All);
        //    var lastExisting = GetManifestEntries(services, pf, PointerFileEntryFilter.LastExisting);
        //    var lastWithDeleted = GetManifestEntries(services, pf, PointerFileEntryFilter.LastWithDeleted);

        //    Assert.AreEqual(6 + 2, all.Count());
        //    Assert.AreEqual(4 + 0, lastExisting.Count());

        //    var relativeNameOfOriginalPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, originalPointerFileInfoFullName);
        //    var relativeNameOfMovedPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pointerFileInfo.FullName);

        //    var originalEntry = lastWithDeleted.Single(lcf => lcf.RelativeName == relativeNameOfOriginalPointerFile);
        //    var movedEntry = lastExisting.Single(lcf => lcf.RelativeName == relativeNameOfMovedPointerFile);

        //    Assert.AreEqual(true, originalEntry.IsDeleted);
        //    Assert.AreEqual(false, movedEntry.IsDeleted);
        //}


        private ServiceProvider ArchiveCommand(bool executeAsCli, AccessTier tier, bool keepLocal = true, int minSize = 0, bool simulate = false, bool dedup = false)
        {
            if (executeAsCli)
            {
                TestSetup.ExecuteCommandline($"archive " +
                                             $"-n {TestSetup.accountName} " +
                                             $"-k {TestSetup.accountKey} " +
                                             $"-p {TestSetup.passphrase} " +
                                             $"-c {TestSetup._container.Name} " +
                                             $"{(keepLocal ? "--keep-local" : "")} --tier hot {TestSetup.rootDirectoryInfo.FullName}");
                throw new NotImplementedException();
            }
            else
            {
                var options = GetArchiveOptions(TestSetup.accountName, TestSetup.accountKey, TestSetup.passphrase, TestSetup._container.Name,
                    keepLocal, tier.ToString(), minSize, simulate, TestSetup.rootDirectoryInfo.FullName);

                var configurationRoot = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string> { { "TempDirName", ".ariustemp" } })
                    .Build();

                var config = new Configuration(options, configurationRoot);

                var pcp = new ParsedCommandProvider { CommandExecutorOptions = options, CommandExecutorType = typeof(ArchiveCommandExecutor) };

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

        private PointerFile GetPointerFileOfLocalContentFile(ServiceProvider services, FileInfo binaryFile)
        {
            var pf = new PointerFile(binaryFile.Directory, binaryFile.GetPointerFileInfo());

            var hvp = services.GetService<IHashValueProvider>();
            var hv = hvp.GetHashValue(pf);
            pf.Hash = hv;

            return pf;
        }
        //private IPointerFile GetPointerFile(ServiceProvider services, FileInfo pointerFileFileInfo)
        //{
        //    var lrr = services.GetRequiredService<LocalRootRepository>();
        //    var pointerFile = lrr.GetAll().OfType<IPointerFile>().Single(pf => pf.FullName == pointerFileFileInfo.FullName);

        //    return pointerFile;
        //}

        //enum PointerFileEntryFilter { All, LastWithDeleted, LastExisting }

        //private async Task<IEnumerable<AzureRepository.PointerFileEntry>> GetManifestEntries(AzureRepository repo, PointerFile pointerFile, PointerFileEntryFilter whichones)
        //{
        //    switch (whichones)
        //    {
        //        case PointerFileEntryFilter.All:
        //            throw new NotImplementedException();
        //            //return ms.ReadManifestFile(mf).PointerFileEntries;
        //        case PointerFileEntryFilter.LastWithDeleted:
        //            return await repo.GetCurrentEntriesAsync(true, pointerFile.Hash);
        //        case PointerFileEntryFilter.LastExisting:
        //            return await repo.GetCurrentEntriesAsync(false, pointerFile.Hash);
        //        default:
        //            throw new ArgumentOutOfRangeException(nameof(whichones), whichones, null);
        //    }
        //}

        ////private Manifest GetManifest(LocalRootRepository lrr, LocalManifestFileRepository lmfr, ManifestService ms, FileInfo localContentFileFileInfo)
        ////{
        ////    var pf = lrr.GetAll().OfType<IPointerFile>().Single(ipf => ipf.LocalContentFileInfo.FullName == localContentFileFileInfo.FullName);
        ////    return ms.ReadManifestFile(lmfr.GetById(pf.Hash));
        ////}


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
