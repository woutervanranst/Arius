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
    class AriusRepositorySteps : TestBase
    {
        public AriusRepositorySteps(ScenarioContext sc) : base(sc)
        {
        }

        [Then("{int} additional Chunk(s) and Manifest(s)")]
        public void ThenAdditionalChunksAndManifests(int x)
        {
            var rs0 = AriusRepository.Stats.SkipLast(1).Last();
            var rs1 = AriusRepository.Stats.Last();

            (rs0.ChunkCount + x).Should().Be(rs1.ChunkCount);
            (rs0.BinaryCount + x).Should().Be(rs1.BinaryCount);
        }
    }
}
