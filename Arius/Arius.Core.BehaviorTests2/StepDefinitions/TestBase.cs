using Arius.Core.Models;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{
    [Binding]
    class TestBase
    {
        public TestBase(ScenarioContext sc)
        {
            scenarioContext = sc;
        }

        protected readonly ScenarioContext scenarioContext;
    }
}
