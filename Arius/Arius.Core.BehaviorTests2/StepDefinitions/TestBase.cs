using Arius.Core.Models;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{
    [Binding]
    class TestBase
    {
        protected record Directories(DirectoryInfo Root, DirectoryInfo RunRoot, DirectoryInfo SourceDirectory, DirectoryInfo TestDirectory);

        public TestBase(ScenarioContext sc)
        {
            scenarioContext = sc;
            directories = sc.ScenarioContainer.Resolve<Directories>();
        }

        protected readonly Directories directories;
        protected readonly ScenarioContext scenarioContext;
    }
}
