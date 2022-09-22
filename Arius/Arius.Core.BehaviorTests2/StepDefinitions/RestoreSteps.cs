using Arius.Core.Commands;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BoDi;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
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

    class Restore_Tests
    {
        //[Test]
        //public void Restore_SynchronizeFile_ValidationException([Values(true, false)] bool download) //Synchronize flag only valid on directory
        //{
        //    var fn = Path.Combine(FileSystem.RestoreDirectory.FullName, "ha.pointer.arius");
        //    File.WriteAllText(fn, "");

        //    Assert.CatchAsync<ValidationException>(async () => await Arius.RestoreCommandAsyc(synchronize: true, download: download, path: fn));

        //    File.Delete(fn);
        //}

    }
}
