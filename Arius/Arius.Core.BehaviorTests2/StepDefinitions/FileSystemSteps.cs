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
    class FileSystemSteps : TestBase
    {
        public FileSystemSteps(ScenarioContext sc) : base(sc)
        {
        }

        [StepArgumentTransformation]
        public static AccessTier TierTransform(string tier) => (AccessTier)tier;


        [Given(@"a local file {string} of size {string} is archived to {word}")]
        public async Task GivenALocalFileOfSizeIsArchivedTo(string relativeName, string size, AccessTier tier)
        {
            CreateFile(relativeName, size);

            await AriusRepository.ArchiveCommandAsync(tier);
        }
        [Given(@"the following local files are archived to {word} tier:")]
        public async Task GivenTheFollowingLocalFilesAreArchivedToTier(AccessTier tier, Table table)
        {
            var files = table.CreateSet<FileTableEntry>().ToList();

            foreach (var f in files)
                CreateFile(f.RelativeName, f.Size);

            await AriusRepository.ArchiveCommandAsync(tier);
        }
        record FileTableEntry(string RelativeName, string Size);
        private static void CreateFile(string relativeName, string size)
        {
            var sizeInBytes = size switch
            {
                "BELOW_ARCHIVE_TIER_LIMIT"
                    => 12 * 1024 + 1, // 12 KB
                "ABOVE_ARCHIVE_TIER_LIMIT"
                    => 1024 * 1024 + 1, // Note: the file needs to be big enough (> 1 MB) to put into Archive storage (see ChunkBlobBase.SetAccessTierPerPolicyAsync)
                _ when
                    // see https://stackoverflow.com/a/3513858
                    // see https://codereview.stackexchange.com/a/67506
                    int.TryParse(Regex.Match(size, @"(?<size>\d*) KB").Groups["size"].Value, out var size0)
                    => size0,
                _ =>
                    throw new ArgumentOutOfRangeException()
            };

            if (FileSystem.Exists(relativeName))
                if (FileSystem.Length(relativeName) != sizeInBytes)
                    throw new ArgumentException("File already exists and is of different size");

            FileSystem.CreateFile(relativeName, sizeInBytes);
        }

    }
}
