using Azure.Storage.Blobs;
using BoDi;
using NUnit.Framework;
using System;
using TechTalk.SpecFlow;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    [Binding]
    public class LocalRepositorySteps
    {
        [BeforeTestRun(Order = 2)]
        public static void InitializeLocalRepository(IObjectContainer oc, BlobContainerClient bcc)
        {
            var unitTestRoot = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "arius", bcc.Name));

            var sourceFolder = unitTestRoot.CreateSubdirectory("source");
            var archiveTestDirectory = unitTestRoot.CreateSubdirectory("archive");
            var restoreTestDirectory = unitTestRoot.CreateSubdirectory("restore");

            oc.RegisterInstanceAs<DirectoryInfo>(sourceFolder, "SourceFolder");
            oc.RegisterInstanceAs<DirectoryInfo>(archiveTestDirectory, "ArchiveTestDirectory");
            oc.RegisterInstanceAs<DirectoryInfo>(restoreTestDirectory, "RestoreTestDirectory");
        }

        public LocalRepositorySteps(IObjectContainer oc)
        {
            archiveTestDirectory = oc.Resolve<DirectoryInfo>("ArchiveTestDirectory");
        }

        private readonly DirectoryInfo archiveTestDirectory;

        [Given(@"one local file")]
        public void GivenOneLocalFile()
        {
            // throw new PendingStepException();
        }
    }
}
