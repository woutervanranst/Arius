using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NUnit.Framework.Internal;

// https://www.automatetheplanet.com/nunit-cheat-sheet/

namespace Arius.Tests
{
    public partial class ArchiveRestoreTests
    {
        [OneTimeSetUp]
        public void ClassInit_Archive()
        {
            // Executes once for the test class. (Optional)

            if (TestSetup.archiveTestDirectory.Exists) TestSetup.archiveTestDirectory.Delete(true);
            TestSetup.archiveTestDirectory.Create();
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
        /// 11/ The chunk is in the appropriate tier
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
        [Test, Order(100)]
        public async Task Archive_FirstFile()
        {
            AccessTier tier = AccessTier.Cool;

            //SET UP -- Copy First file to the temp folder
            var bfi1 = TestSetup.sourceFolder.GetFiles().First();
            bfi1 = bfi1.CopyTo(TestSetup.archiveTestDirectory);

            //EXECUTE
            var services = await ArchiveCommand(tier, dedup: false);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());
            //11
            Assert.AreEqual(tier, repo.GetAllChunkBlobItems().First().AccessTier);
            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            //30
            var pfes = await repo.GetCurrentEntries(true);
            Assert.AreEqual(1, pfes.Count());

            //31
            var pf1 = GetPointerFileOfLocalContentFile(bfi1);
            Assert.IsTrue(File.Exists(pf1.FullName));

            //40
            var pfe1 = pfes.First();
            Assert.AreEqual(Path.GetRelativePath(TestSetup.archiveTestDirectory.FullName, pf1.FullName), pfe1.RelativeName);

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
        [Test, Order(200)]
        public async Task Archive_SecondFileDuplicate()
        {
            //SET UP
            //Add a duplicate of the first file
            var bfi1 = TestSetup.archiveTestDirectory.GetBinaryFiles().First();
            var bfi2 = bfi1.CopyTo(TestSetup.archiveTestDirectory, $"Copy of {bfi1.Name}");

            // Modify datetime slightly
            bfi2.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            bfi2.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux


            //EXECUTE
            var services = await ArchiveCommand(AccessTier.Cool);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());
            
            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            //30
            var pfes = await repo.GetCurrentEntries(true);
            Assert.AreEqual(1 + 1, pfes.Count());

            //31
            var pf1 = GetPointerFileOfLocalContentFile(bfi1);
            var pf2 = GetPointerFileOfLocalContentFile(bfi2);
            Assert.IsTrue(File.Exists(pf1.FullName));
            Assert.IsTrue(File.Exists(pf2.FullName));

            //40
            var pfe2 = pfes.Single(pfe => pfe.RelativeName == Path.GetRelativePath(TestSetup.archiveTestDirectory.FullName, pf2.FullName));

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
        [Test, Order(300)]
        public async Task Archive_JustAPointer()
        {
            //SET UP
            //Add a duplicate of the pointer
            var pfi1 = TestSetup.archiveTestDirectory.GetPointerFiles().First();
            var pfi3 = Arius.Extensions.FileInfoExtensions.CopyTo(pfi1, $"Copy2 of {pfi1.Name}");

            // Modify datetime slightly
            pfi3.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            pfi3.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux


            //EXECUTE
            var services = await ArchiveCommand(AccessTier.Cool);


            //ASSERT OUTCODE
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            //30
            var pfes = await repo.GetCurrentEntries(true);
            Assert.AreEqual(2 + 1, pfes.Count());

            //31
            Assert.IsTrue(pfi1.Exists);
            Assert.IsTrue(pfi3.Exists);

            //40
            var pfe3 = pfes.Single(pfe => pfe.RelativeName == Path.GetRelativePath(TestSetup.archiveTestDirectory.FullName, pfi3.FullName));

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
        [Test, Order(400)]
        public async Task Archive_RenameBinaryFileWithPointer()
        {
            //SET UP
            var bfi1 = TestSetup.archiveTestDirectory.GetBinaryFiles().First();
            var pfi1 = bfi1.GetPointerFileInfo();
            var pfi1_FullName_Original = pfi1.FullName;


            //Rename BinaryFile + Pointer
            bfi1.Rename($"Moving of {bfi1.Name}");
            pfi1.Rename($"Moving of {pfi1.Name}");


            //EXECUTE
            var services = await ArchiveCommand(AccessTier.Cool);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            //30
            var lastExistingPfes = (await repo.GetCurrentEntries(false)).ToList();
            Assert.AreEqual(3 + 0, lastExistingPfes.Count);

            //31
            var lastWithDeletedPfes = (await repo.GetCurrentEntries(true)).ToList();
            Assert.AreEqual(3 + 1, lastWithDeletedPfes.Count);

            //var all = GetManifestEntries(services, pf, PointerFileEntryFilter.All);
            //Assert.AreEqual(3 + 2, all.Count());

            //40
            var pfi1_Relativename_Original = Path.GetRelativePath(TestSetup.archiveTestDirectory.FullName, pfi1_FullName_Original);
            var originalPfe = lastWithDeletedPfes.Single(pfe => pfe.RelativeName == pfi1_Relativename_Original);
            Assert.AreEqual(true, originalPfe.IsDeleted);

            //41
            Assert.IsNull(lastExistingPfes.SingleOrDefault(pfe => pfe.RelativeName == pfi1_Relativename_Original));

            //42
            var pfi1_Relativename_AfterMove = Path.GetRelativePath(TestSetup.archiveTestDirectory.FullName, pfi1.FullName);
            var movedPfe = lastExistingPfes.Single(lcf => lcf.RelativeName == pfi1_Relativename_AfterMove);
            Assert.AreEqual(false, movedPfe.IsDeleted);
        }

        /// <summary>
        /// Rename a BinaryFile without renaming the pointer.
        /// Expectation: this will become essentially a "duplicate"
        /// 
        /// 10/ One manifest and one binary should still be there
        /// 20/ No additional ManifestHash is created (ie just 1)
        /// 30*/ One additional PointerFileEntry (ie for the moved file)
        /// 40*/ Both the Original file and the Moved file are not marked as Deleted
        /// </summary>
        /// <returns></returns>
        [Test, Order(500)]
        public async Task Archive_RenameBinaryFileFileWithoutPointer()
        {
            //SET UP
            var bfi = TestSetup.archiveTestDirectory.GetBinaryFiles().First();
            var pfi = bfi.GetPointerFileInfo();
            var pfi_FullName_Original = pfi.FullName;
            bfi.Rename($"Moving of {bfi.Name}");
            //TestSetup.MoveFile(pointerFileInfo, $"Moving of {pointerFileInfo.Name}"); <-- Dit doen we hier NIET vs de vorige


            //EXECUTE
            var services = await ArchiveCommand(AccessTier.Cool);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            var pfes_OnlyExisting = (await repo.GetCurrentEntries(includeDeleted: false)).ToList();
            var pfes_WithDeleted = (await repo.GetCurrentEntries(includeDeleted: true)).ToList();

            //30
            Assert.AreEqual(3 + 1, pfes_OnlyExisting.Count);
            Assert.AreEqual(4 + 1, pfes_WithDeleted.Count);

            //Get the PointerFileNetries of the original and moved file
            var pfi_RelativeName_Original = Path.GetRelativePath(TestSetup.archiveTestDirectory.FullName, pfi_FullName_Original);
            var pfi_RelativeName_Moved = Path.GetRelativePath(TestSetup.archiveTestDirectory.FullName, pfi.FullName);

            var pfe_Original = pfes_WithDeleted.Single(lcf => lcf.RelativeName == pfi_RelativeName_Original);
            var pfe_Moved = pfes_OnlyExisting.Single(lcf => lcf.RelativeName == pfi_RelativeName_Moved);

            //40
            Assert.AreEqual(false, pfe_Original.IsDeleted);
            Assert.AreEqual(false, pfe_Moved.IsDeleted);
        }

        /// <summary>
        /// Archive with removal of local files
        /// 
        /// 10/ Local BinaryFiles exist
        /// 20/ After running archive, the BinaryFiles no longer exist
        /// </summary>
        /// <returns></returns>
        [Test, Order(600)]
        public async Task Archive_RemoveBinaryFiles()
        {
            //10
            Assert.IsTrue(TestSetup.archiveTestDirectory.GetBinaryFiles().Any());

            
            //EXECUTE
            await ArchiveCommand(AccessTier.Cool, removeLocal: true);


            //20
            Assert.IsTrue(!TestSetup.archiveTestDirectory.GetBinaryFiles().Any());
        }

        /// <summary>
        /// Rename a PointerFile without the BinaryFile (since it is no longer present)
        /// Expectation: essentially a "move"
        /// 
        /// 10/ One manifest and one binary should still be there
        /// 20/ No additional ManifestHash is created (ie just 1)
        /// 30*/ No additional EXISTING PointerFileEntry is created (1 new + 1 deleted = 0 net increase)
        /// 31*/ One additional with deleted PointerFileEntry is created (1 deleted)
        /// 40/ The original file is marked as deleted
        /// 41/ The moved file is not marked as deleted
        /// </summary>
        /// <returns></returns>
        [Test, Order(700)]
        public async Task Archive_RenameJustPointer()
        {
            //SET UP
            var pfi = TestSetup.archiveTestDirectory.GetPointerFiles().First();
            var pfi_FullName_Original = pfi.FullName;
            pfi.Rename($"Moving2 of {pfi.Name}");


            //EXECUTE
            var services = await ArchiveCommand(AccessTier.Cool);


            //ASSERT OUTCOME
            var repo = services.GetRequiredService<AzureRepository>();

            //10
            Assert.AreEqual(1, repo.GetAllChunkBlobItems().Count());

            //20
            Assert.AreEqual(1, repo.GetAllManifestHashes().Count());

            var pfes_OnlyExisting = (await repo.GetCurrentEntries(false)).ToList();
            var pfes_WithDeleted = (await repo.GetCurrentEntries(true)).ToList();

            //30
            Assert.AreEqual(4 + 0, pfes_OnlyExisting.Count);
            //31
            Assert.AreEqual(5 + 1, pfes_WithDeleted.Count);

            //Get the PointerFileNetries of the original and moved file
            var pfi_RelativeName_Original = Path.GetRelativePath(TestSetup.archiveTestDirectory.FullName, pfi_FullName_Original);
            var pfi_RelativeName_Moved = Path.GetRelativePath(TestSetup.archiveTestDirectory.FullName, pfi.FullName);

            var pfe_Original = pfes_WithDeleted.Single(lcf => lcf.RelativeName == pfi_RelativeName_Original);
            var pfe_Moved = pfes_OnlyExisting.Single(lcf => lcf.RelativeName == pfi_RelativeName_Moved);

            //40
            Assert.AreEqual(true, pfe_Original.IsDeleted);
            //41
            Assert.AreEqual(false, pfe_Moved.IsDeleted);
        }

        [Test, Order(1000)]
        public async Task Archive_FullSourceDirectory()
        {
            // Empty the test directory
            TestSetup.archiveTestDirectory.Delete(true);
            TestSetup.archiveTestDirectory.Create();
            TestSetup.sourceFolder.CopyTo(TestSetup.archiveTestDirectory);

            //EXECUTE
            var services = await ArchiveCommand(AccessTier.Cool);
        }




        private async Task<IServiceProvider> ArchiveCommand(AccessTier tier, bool removeLocal = false, bool fastHash = false, bool dedup = false)
        {
            var cmd = "archive " +
                $"-n {TestSetup.accountName} " +
                $"-k {TestSetup.accountKey} " +
                $"-p {TestSetup.passphrase} " +
                $"-c {TestSetup.container.Name} " +
                $"{(removeLocal ? "--remove-local " : "")}" +
                $"--tier {tier.ToString().ToLower()} " +
                $"{(dedup ? "--dedup " : "")}" +
                $"{(fastHash ? "--fasthash" : "")}" +
                $"{TestSetup.archiveTestDirectory.FullName}";

            return await ExecuteCommand(cmd);   
        }

        private async Task<IServiceProvider> ExecuteCommand(string cmd)
        {
            Environment.SetEnvironmentVariable(Arius.AriusCommandService.CommandLineEnvironmentVariableName, cmd);

            //Action<IConfigurationBuilder> bla = (b) =>
            //{
            //    b.AddInMemoryCollection(new Dictionary<string, string> {
            //            { "TempDir:TempDirectoryName", ".ariustemp2" }
            //        });
            //};

            await Arius.Program.CreateHostBuilder(cmd.Split(' '), null).RunConsoleAsync();

            if (Environment.ExitCode != 0)
                throw new ApplicationException("Exitcode is not 0");

            var aro = new AzureRepositoryOptions()
            {
                AccountName = TestSetup.accountName,
                AccountKey = TestSetup.accountKey,
                Container = TestSetup.container.Name,
                Passphrase = TestSetup.passphrase
            };

            var sc = new ServiceCollection()
                .AddSingleton<ICommandExecutorOptions>(aro)
                .AddSingleton<AzureRepository>()
                .AddSingleton<Services.IBlobCopier, Services.AzCopier>()

                .AddSingleton<ILoggerFactory, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory>()
                .AddLogging()

                .BuildServiceProvider();

            return sc;
        }

        private class AzureRepositoryOptions : AzureRepository.IAzureRepositoryOptions, Services.IAzCopyUploaderOptions
        {
            public string AccountName { get; init; }
            public string AccountKey { get; init; }
            public string Container { get; init; }
            public string Passphrase { get; init; }
        }

        //private ArchiveOptions GetArchiveOptions(string accountName, string accountKey, string passphrase, string container, bool removeLocal, string tier, bool fastHash, string path)
        //{
        //    return new()
        //    {
        //        AccountName = accountName,
        //        AccountKey = accountKey,
        //        Passphrase = passphrase,
        //        FastHash = fastHash,
        //        Container = container,
        //        RemoveLocal = removeLocal,
        //        Tier = tier,
        //        //MinSize = minSize,
        //        //Simulate = simulate,
        //        Path = path
        //    };
        //}

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


        // * archive a file for which ONLY the chunk (not deduped) exists (ie no pointer, no entries no manifest)
        // * archive a duplicated chunkfile
        // * chunk1, 2, 3 are already uploaded. file 2 = chunk 2,3. archive.
        //         *
        //         */
    }
}
