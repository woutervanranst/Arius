namespace Arius.Core.BehaviorTests.StepDefinitions;

[Binding]
class TestBase
{
    public TestBase(ScenarioContext sc)
    {
        scenarioContext = sc;
    }

    protected readonly ScenarioContext scenarioContext;
}