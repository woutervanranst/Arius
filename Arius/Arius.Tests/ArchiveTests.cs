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


        /// <summary>
        /// Archive a file
        /// Expectation: 
        /// 10/ 1 Chunk was uploaded
        /// 20/ 1 ManifestHash exists
        /// 30/ 1 PointerFileEntry exists
        /// 31/ 1 PointerFile is created
        /// 40/ The RelativeName of the PointerFile matches with the PointerFileEntry
        /// 41/ The PointerFileEntry is not marked as deleted
        /// 42/ The Creation- and LastWriteTimeUtc match
        /// 
        /// 
        /// </summary>
        /// <returns></returns>
        [Test, Order(10)]
        public async Task ArchiveFirstFile()
        {
            //SET UP -- Copy First file to the temp folder
            var bfi1 = TestSetup.sourceFolder.GetFiles().First();
            bfi1 = TestSetup.CopyFile(bfi1, TestSetup.rootDirectoryInfo);


            //EXECUTE
            var services = ArchiveCommand(false, AccessTier.Cool, dedup: false);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());
            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            //30
            var pfes = await repo.GetCurrentEntriesAsync(true);
            Assert.AreEqual(1, pfes.Count());

            //31
            var pf1 = GetPointerFileOfLocalContentFile(bfi1);
            Assert.IsTrue(File.Exists(pf1.FullName));

            //40
            var pfe1 = pfes.First();
            Assert.AreEqual(Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pf1.FullName), pfe1.RelativeName);

            //41
            Assert.AreEqual(false, pfe1.IsDeleted);

            //42
            Assert.AreEqual(bfi1.CreationTimeUtc, pfe1.CreationTimeUtc);
            Assert.AreEqual(bfi1.LastWriteTimeUtc, pfe1.LastWriteTimeUtc);
        }

        /// <summary>
        /// Duplicate the first file and archive again (one addtl pointer, yet no addtl upload)
        /// 
        /// Expectation:
        /// 10/ No additional chunks were uploaded (ie just 1)
        /// 20/ No additional ManifestHash is created (ie just 1)
        /// 30*/ 1 addtl PointerFileEntry is created
        /// 31*/ A new PointerFile is created
        /// 40/ A PointerFileEntry with the matching relativeName exists
        /// 41/ The PointerFileEntry is not marked as deleted
        /// 42/ The Creation- and LastWriteTimeUtc match
        /// </summary>
        /// <returns></returns>
        [Test, Order(20)]
        public async Task ArchiveSecondFileDuplicate()
        {
            //SET UP
            //Add a duplicate of the first file
            var bfi1 = TestSetup.rootDirectoryInfo.GetBinaryFiles().First();
            var bfi2 = TestSetup.CopyFile(bfi1, TestSetup.rootDirectoryInfo, $"Copy of {bfi1.Name}");

            // Modify datetime slightly
            bfi2.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            bfi2.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux


            //EXECUTE
            var services = ArchiveCommand(false, AccessTier.Cool);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());
            
            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            //30
            var pfes = await repo.GetCurrentEntriesAsync(true);
            Assert.AreEqual(1 + 1, pfes.Count());

            //31
            var pf1 = GetPointerFileOfLocalContentFile(bfi1);
            var pf2 = GetPointerFileOfLocalContentFile(bfi2);
            Assert.IsTrue(File.Exists(pf1.FullName));
            Assert.IsTrue(File.Exists(pf2.FullName));

            //40
            var pfe2 = pfes.Single(pfe => pfe.RelativeName == Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pf2.FullName));

            //41
            Assert.AreEqual(false, pfe2.IsDeleted);

            //42
            Assert.AreEqual(bfi2.CreationTimeUtc, pfe2.CreationTimeUtc);
            Assert.AreEqual(bfi2.LastWriteTimeUtc, pfe2.LastWriteTimeUtc);
        }

        /// <summary>
        /// Archive just a pointer (duplicate an existing pointer, one addtl PointerFileEntry should exist)
        /// 
        /// Expectation:
        /// 10/ No additional chunks were uploaded (ie just 1)
        /// 20/ No additional ManifestHash is created (ie just 1)
        /// 30*/ 1 addtl PointerFileEntry is created
        /// 31/ Both PointerFiles still exist
        /// 40/ A PointerFileEntry with the matching relativeName exists
        /// 41/ The PointerFileEntry is not marked as deleted
        /// 42/ The Creation- and LastWriteTimeUtc match
        /// </summary>
        [Test, Order(30)]
        public void ArchiveJustAPointer()
        {
            //SET UP
            //Add a duplicate of the pointer
            var pfi1 = TestSetup.rootDirectoryInfo.GetPointerFiles().First();
            var pfi3 = TestSetup.CopyFile(pfi1, $"Copy2 of {pfi1.Name}");

            // Modify datetime slightly
            pfi3.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            pfi3.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux


            //EXECUTE
            var services = ArchiveCommand(false, AccessTier.Cool);


            //ASSERT OUTCODE
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            //30
            var pfes = repo.GetCurrentEntries(true);
            Assert.AreEqual(2 + 1, pfes.Count());

            //31
            Assert.IsTrue(pfi1.Exists);
            Assert.IsTrue(pfi3.Exists);

            //40
            var pfe3 = pfes.Single(pfe => pfe.RelativeName == Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pfi3.FullName));

            //41
            Assert.AreEqual(false, pfe3.IsDeleted);

            //42
            Assert.AreEqual(pfi3.CreationTimeUtc, pfe3.CreationTimeUtc);
            Assert.AreEqual(pfi3.LastWriteTimeUtc, pfe3.LastWriteTimeUtc);
        }

        /// <summary>
        /// Rename a previously archived pointer and binary file (old pointerfileentries marked as deleted, no net increase in current)
        /// 
        /// 10/ No additional chunks were uploaded (ie just 1)
        /// 20/ No additional ManifestHash is created (ie just 1)
        /// 30*/ No net increase in current PointerFileEntries (ie still 3)
        /// 31*/ One PointerFileEntry is marked as deleted, bringing the total to 4
        /// 40*/ The original PointerFileEntry is marked as deleted
        /// 41*/ No current entry exists for the original pointerfile
        /// 42*/ A new PointerFileEntry exists that is not marked as deleted
        /// </summary>
        [Test, Order(40)]
        public void RenameBinaryFileWithPointer()
        {
            //SET UP
            var bfi1 = TestSetup.rootDirectoryInfo.GetBinaryFiles().First();
            var pfi1 = bfi1.GetPointerFileInfo();
            var pfi1_FullName_Original = pfi1.FullName;


            //Rename BinaryFile + Pointer
            TestSetup.MoveFile(bfi1, $"Moving of {bfi1.Name}");
            TestSetup.MoveFile(pfi1, $"Moving of {pfi1.Name}");


            //EXECUTE
            var services = ArchiveCommand(false, AccessTier.Cool);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            //30
            var lastExistingPfes = repo.GetCurrentEntries(false).ToList();
            Assert.AreEqual(3 + 0, lastExistingPfes.Count());

            //31
            var lastWithDeletedPfes = repo.GetCurrentEntries(true).ToList();
            Assert.AreEqual(3 + 1, lastWithDeletedPfes.Count);

            //var all = GetManifestEntries(services, pf, PointerFileEntryFilter.All);
            //Assert.AreEqual(3 + 2, all.Count());

            //40
            var pfi1_Relativename_Original = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pfi1_FullName_Original);
            var originalPfe = lastWithDeletedPfes.Single(pfe => pfe.RelativeName == pfi1_Relativename_Original);
            Assert.AreEqual(true, originalPfe.IsDeleted);

            //41
            Assert.IsNull(lastExistingPfes.SingleOrDefault(pfe => pfe.RelativeName == pfi1_Relativename_Original));

            //42
            var pfi1_Relativename_AfterMove = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pfi1.FullName);
            var movedPfe = lastExistingPfes.Single(lcf => lcf.RelativeName == pfi1_Relativename_AfterMove);
            Assert.AreEqual(false, movedPfe.IsDeleted);
        }

        [Test, Order(50)]
        public void RenameLocalContentFileWithoutPointer()
        {
            //Modify temp folder
            //Rename a file
            var localContentFileFileInfo = TestSetup.rootDirectoryInfo.GetBinaryFiles().First();
            var pointerFileInfo = localContentFileFileInfo.GetPointerFileInfo(); // new FileInfo(originalFileFullName + ".pointer.arius");
            var originalPointerFileInfoFullName = pointerFileInfo.FullName;

            TestSetup.MoveFile(localContentFileFileInfo, $"Moving of {localContentFileFileInfo.Name}");
            //TestSetup.MoveFile(pointerFileInfo, $"Moving of {pointerFileInfo.Name}"); <-- Dit doen we hier NIET vs de vorige


            //Execute Archive
            var services = ArchiveCommand(false, AccessTier.Cool);


            //Check outcome
            var repo = services.GetRequiredService<AzureRepository>();

            //One manifest and one binary should still be there
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //Get the manifest entries
            //    var pf = GetPointerFile(services, pointerFileInfo);
            //    var all = GetManifestEntries(services, pf, PointerFileEntryFilter.All);
            var lastExisting = repo.GetCurrentEntries(false).ToList();
            var lastWithDeleted = repo.GetCurrentEntries(true).ToList();

            Assert.AreEqual(3 + 1, lastExisting.Count());
            Assert.AreEqual(4 + 1, lastWithDeleted.Count());
            //    Assert.AreEqual(5 + 1, all.Count());

            var relativeNameOfOriginalPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, originalPointerFileInfoFullName);
            var relativeNameOfMovedPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pointerFileInfo.FullName);

            var originalEntry = lastWithDeleted.Single(lcf => lcf.RelativeName == relativeNameOfOriginalPointerFile);
            var movedEntry = lastExisting.Single(lcf => lcf.RelativeName == relativeNameOfMovedPointerFile);

            Assert.AreEqual(false, originalEntry.IsDeleted);
            Assert.AreEqual(false, movedEntry.IsDeleted);
        }

        [Test, Order(55)]
        public void TestRemoveLocal()
        {
            Assert.IsTrue(TestSetup.rootDirectoryInfo.GetBinaryFiles().Any());

            ArchiveCommand(false, AccessTier.Cool, removeLocal: true);

            Assert.IsTrue(!TestSetup.rootDirectoryInfo.GetBinaryFiles().Any());
        }

        [Test, Order(60)]
        public void RenameJustPointer()
        {
            //Modify temp folder
            //Rename a file
            var pointerFileInfo = TestSetup.rootDirectoryInfo.GetPointerFiles().First();
            var originalPointerFileInfoFullName = pointerFileInfo.FullName;

            //TestSetup.MoveFile(localContentFileFileInfo, $"Moving of {localContentFileFileInfo.Name}");
            TestSetup.MoveFile(pointerFileInfo, $"Moving of {pointerFileInfo.Name}"); //< --Dit doen we hier NIET vs de vorige


            //Execute Archive
            var services = ArchiveCommand(false, AccessTier.Cool);


            //Check outcome
            var repo = services.GetRequiredService<AzureRepository>();

            //One manifest and one binary should still be there
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //Get the manifest entries
            //var pf = GetPointerFile(services, pointerFileInfo);
            //var all = GetManifestEntries(services, pf, PointerFileEntryFilter.All);
            //var lastExisting = GetManifestEntries(services, pf, PointerFileEntryFilter.LastExisting);
            //var lastWithDeleted = GetManifestEntries(services, pf, PointerFileEntryFilter.LastWithDeleted);
            var lastExisting = repo.GetCurrentEntries(false).ToList();
            var lastWithDeleted = repo.GetCurrentEntries(true).ToList();

            Assert.AreEqual(4 + 0, lastExisting.Count());
            Assert.AreEqual(5 + 1, lastWithDeleted.Count());
            //Assert.AreEqual(6 + 2, all.Count());


            var relativeNameOfOriginalPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, originalPointerFileInfoFullName);
            var relativeNameOfMovedPointerFile = Path.GetRelativePath(TestSetup.rootDirectoryInfo.FullName, pointerFileInfo.FullName);

            var originalEntry = lastWithDeleted.Single(lcf => lcf.RelativeName == relativeNameOfOriginalPointerFile);
            var movedEntry = lastExisting.Single(lcf => lcf.RelativeName == relativeNameOfMovedPointerFile);

            Assert.AreEqual(true, originalEntry.IsDeleted);
            Assert.AreEqual(false, movedEntry.IsDeleted);
        }


        private ServiceProvider ArchiveCommand(bool executeAsCli, AccessTier tier, bool removeLocal = false, bool fastHash = false, bool dedup = false)
        {
            if (executeAsCli)
            {
                TestSetup.ExecuteCommandline($"archive " +
                                             $"-n {TestSetup.accountName} " +
                                             $"-k {TestSetup.accountKey} " +
                                             $"-p {TestSetup.passphrase} " +
                                             $"-c {TestSetup._container.Name} " +
                                             $"{(removeLocal ? "--remove-local" : "")} --tier hot {TestSetup.rootDirectoryInfo.FullName}");
                throw new NotImplementedException();
            }
            else
            {
                var options = GetArchiveOptions(TestSetup.accountName, TestSetup.accountKey, TestSetup.passphrase, TestSetup._container.Name,
                    removeLocal, tier.ToString(), fastHash, TestSetup.rootDirectoryInfo.FullName);

                var configurationRoot = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string> { 
                        { "TempDirName", ".ariustemp" },
                        { "UploadTempDirName", ".ariustempupload" }
                    })
                    .Build();

                var config = new Configuration(options, configurationRoot);

                var pcp = new ParsedCommandProvider { CommandExecutorOptions = options, CommandExecutorType = typeof(ArchiveCommandExecutor) };

                var services = Program.GetServiceProvider(config, pcp);

                var exec = services.GetRequiredService<ArchiveCommandExecutor>();

                exec.Execute();

                return services;
            }

        }

        private ArchiveOptions GetArchiveOptions(string accountName, string accountKey, string passphrase, string container, bool removeLocal, string tier, bool fastHash, string path)
        {
            return new()
            {
                AccountName = accountName,
                AccountKey = accountKey,
                Passphrase = passphrase,
                FastHash = fastHash,
                Container = container,
                RemoveLocal = removeLocal,
                Tier = tier,
                //MinSize = minSize,
                //Simulate = simulate,
                Path = path
            };
        }

        private PointerFile GetPointerFileOfLocalContentFile(FileInfo binaryFile)
        {
            return new PointerFile(binaryFile.Directory, binaryFile.GetPointerFileInfo());
        }
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
