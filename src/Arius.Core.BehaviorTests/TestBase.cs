using Arius.Core.BehaviorTests.StepDefinitions;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.BehaviorTests;

[Binding]
class TestBase
{
    public TestBase(ScenarioContext sc)
    {
        scenarioContext = sc;
    }

    protected readonly ScenarioContext scenarioContext;

    protected Repository Repository => TestSetup.Repository;


    [StepArgumentTransformation]
    public static AccessTier TierTransform(string tier) => (AccessTier)tier;

    [StepArgumentTransformation]
    public static RelativePath RelativePathTransform(string relativePath) => new(relativePath);
}