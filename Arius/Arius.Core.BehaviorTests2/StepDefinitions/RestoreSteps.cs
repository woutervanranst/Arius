using Arius.Core.Commands;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BoDi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Text.RegularExpressions;
using System.Xml;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Assist;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{

    [Binding]
    class RestoreSteps : TestBase
    {
        public RestoreSteps(ScenarioContext sc) : base(sc)
        {
        }

        [When("restored")]
        public async Task WhenRestored()
        {
            await Arius.RestoreCommandAsyc(synchronize: true, download: true, keepPointers: true);
        }

        [Then("all files are restored successfully")]
        public void ThenAllFilesAreRestoreedSuccessfully()
        {
            FileSystem.RestoreDirectoryEqualToArchiveDirectory();
        }




    }
}
