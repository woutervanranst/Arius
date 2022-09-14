using Azure.Storage.Blobs;
using BoDi;
using NUnit.Framework;
using System;
using System.Diagnostics;
using TechTalk.SpecFlow;
using Arius.Core.Extensions;
using Arius.Core.BehaviorTests.Extensions;
using Arius.Core.Repositories;
using static Arius.Core.BehaviorTests.StepDefinitions.ScenarioContextExtensions;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    public record Directories(DirectoryInfo Root, DirectoryInfo RunRoot, DirectoryInfo SourceDirectory, DirectoryInfo ArchiveTestDirectory, DirectoryInfo RestoreTestDirectory);

    [Binding]
    class LocalRepositorySteps : LocalTestBase
    {
        [BeforeTestRun(Order = 2)] //run after the RemoteRepository is initialized, and the BlobContainerClient is available for DI
        public static void InitializeLocalRepository(IObjectContainer oc, BlobContainerClient bcc)
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "arius"));
            var runRoot = root.CreateSubdirectory(bcc.Name);
            var sourceDirectory = runRoot.CreateSubdirectory("source");
            var archiveTestDirectory = runRoot.CreateSubdirectory("archive");
            var restoreTestDirectory = runRoot.CreateSubdirectory("restore");

            oc.RegisterInstanceAs(new Directories(root, runRoot, sourceDirectory, archiveTestDirectory, restoreTestDirectory));
        }

        public LocalRepositorySteps(ScenarioContext sc, Directories directories) : base(sc, directories)
        {
        }

        [BeforeScenario]
        public void ClearDirectories()
        {
            directories.ArchiveTestDirectory.Clear();
            directories.RestoreTestDirectory.Clear();
        }

        [Given(@"a local archive with file {word}")]
        public void GivenOneLocalFile(string fileId)
        {
            var f0 = GetOrCreateSourceFile(fileId);

            var f1 = f0.CopyTo(directories.SourceDirectory, directories.ArchiveTestDirectory);

            scenarioContext[fileId] = new RelatedFiles(f0, f1, null);
        }

        record RelatedFiles(FileInfo Source, FileInfo Archive, FileInfo Restore);

        

        [Given(@"a local archive with file {word} duplicate of {word}")]
        public void GivenOneLocalFileDuplicateOfFile(string newFileId, string originalFileId)
        {
            var f0 = GetOrCreateSourceFile(originalFileId);
            var f1 = f0.CopyTo(directories.SourceDirectory, $"Duplicate of {f0.Name}");
            f1.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            f1.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux

            var f2 = f1.CopyTo(directories.SourceDirectory, directories.ArchiveTestDirectory);

            scenarioContext[newFileId] = new RelatedFiles(f1, f2, null);
        }


        [Then(@"all local files have PointerFiles and PointerFileEntries")]
        public void ThenAllLocalFilesHavePointerFilesAndPointerFileEntries()
        {
            foreach (var bfi in directories.ArchiveTestDirectory.GetBinaryFileInfos())
            {
                var (pf, pfe) = GetPointerInfo(bfi);

                pf.Should().NotBeNull();
                
                pfe.Should().NotBeNull();
                pfe.IsDeleted.Should().BeFalse();

                bfi.CreationTimeUtc.Should().Be(pfe.CreationTimeUtc);
                bfi.LastWriteTimeUtc.Should().Be(pfe.LastWriteTimeUtc);
            }
        }

        [Then(@"all local PointerFiles have PointerFileEntries")]
        public void ThenAllLocalPointerFilesHavePointerFileEntries()
        {
            foreach (var bfi in directories.ArchiveTestDirectory.GetPointerFileInfos())
            {
                var (pf, pfe) = GetPointerInfo(bfi);

                pf.Should().NotBeNull();

                pfe.Should().NotBeNull();
                pfe.IsDeleted.Should().BeFalse();

                bfi.CreationTimeUtc.Should().Be(pfe.CreationTimeUtc);
                bfi.LastWriteTimeUtc.Should().Be(pfe.LastWriteTimeUtc);
            }
        }

        [When(@"the local archive is cleared")]
        public void WhenTheLocalArchiveIsCleared()
        {
            directories.ArchiveTestDirectory.Clear();
        }

        [Then(@"{word} does not have a PointerFile")]
        public void ThenFileDoesNotHaveAPointerFile(string fileId)
        {
            var fi = ((RelatedFiles)scenarioContext[fileId]).Archive;

            var (pf, pfe) = GetPointerInfo(fi);

            pf.Should().BeNull();
        }

        [Then(@"the PointerFileEntry for {word} is marked as deleted")]
        public void ThenThePointerFileEntryForFileIsMarkedAsDeleted(string fileId)
        {
            var fi = ((RelatedFiles)scenarioContext[fileId]).Archive;

            var (pf, pfe) = GetPointerInfo(fi);

            pfe.IsDeleted.Should().BeTrue();
        }


        [Given("a duplicate Pointer of file {word}")]
        public void WhenADuplicatePointerOfFileFile(string fileId)
        {
            var fi = ((RelatedFiles)scenarioContext[fileId]).Archive;

            var (pf0, _) = GetPointerInfo(fi);
            var pf1 = new FileInfo(Path.Combine(directories.ArchiveTestDirectory.FullName, $"Duplicate of {pf0.Name}"));
            File.Copy(pf0.FullName, pf1.FullName);

            pf1.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            pf1.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
        }

        [Then("{int} PointerFile(s) exist")]
        public void ThenPointerFilesExist(int p0)
        {
            directories.ArchiveTestDirectory.GetPointerFileInfos().Count().Should().Be(p0);
        }






        private FileInfo GetOrCreateSourceFile(string fileId, string? name = default, int? sizeInBytes = default)
        {
            if (!scenarioContext.ContainsKey(fileId))
            {
                if (name == default)
                    name = Path.GetRandomFileName();

                if (sizeInBytes == default)
                    sizeInBytes = 512000 + 1;

                var f = CreateRandomFile(Path.Combine(directories.SourceDirectory.FullName, "dir 1", name), sizeInBytes!.Value);

                scenarioContext[fileId] = new RelatedFiles(f, null, null);
            }

            return ((RelatedFiles)scenarioContext[fileId]).Source;
        }

        private static FileInfo CreateRandomFile(string fileFullName, int sizeInBytes)
        {
            // https://stackoverflow.com/q/4432178/1582323

            var f = new FileInfo(fileFullName);
            if (!f.Directory.Exists)
                f.Directory.Create();

            byte[] data = new byte[sizeInBytes];
            var rng = new Random();
            rng.NextBytes(data);
            File.WriteAllBytes(fileFullName, data);

            return f;
        }

        [AfterTestRun]
        public static void OneTimeTearDown(Directories directories)
        {
            // Delete local temp
            foreach (var d in directories.Root.GetDirectories())
                d.Delete(true);
        }
    }
}
