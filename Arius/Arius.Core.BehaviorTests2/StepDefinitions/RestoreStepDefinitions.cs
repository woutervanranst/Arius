using Arius.Core.Commands;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using BoDi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Text.RegularExpressions;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Assist;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{

    [Binding]
    class RestoreStepDefinitions : TestBase
    {
        public RestoreStepDefinitions(ScenarioContext sc) : base(sc)
        {
        }

        [Given(@"the following local files are archived to {word} tier:")]
        public async Task GivenTheFollowingLocalFilesAreArchived(string tier, Table table)
        {
            var files = table.CreateSet<FileTableEntry>().ToList();

            foreach (var f in files)
            {
                var sizeInBytes = f.Size switch
                {
                    "BELOW_ARCHIVE_TIER_LIMIT"
                        => 12 * 1024 + 1, // 12 KB
                    "ABOVE_ARCHIVE_TIER_LIMIT"
                        => 1024 * 1024 + 1, // Note: the file needs to be big enough (> 1 MB) to put into Archive storage (see ChunkBlobBase.SetAccessTierPerPolicyAsync)
                    _ when
                        // see https://stackoverflow.com/a/3513858
                        // see https://codereview.stackexchange.com/a/67506
                        int.TryParse(Regex.Match(f.Size, @"(?<size>\d*) KB").Groups["size"].Value, out var size0)
                        => size0,
                    _ =>
                        throw new ArgumentOutOfRangeException()
                };

                if (FileSystem.Exists(f.RelativeName))
                    if (FileSystem.Length(f.RelativeName) != sizeInBytes)
                        throw new ArgumentException("File already exists and is of different size");

                FileSystem.CreateFile(f.RelativeName, sizeInBytes);
            }
        }

        record FileTableEntry(string Id, string RelativeName, string Size);
    }
}
