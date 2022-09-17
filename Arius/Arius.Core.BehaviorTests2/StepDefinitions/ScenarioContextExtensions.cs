using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{
    static class ScenarioContextExtensions
    {
        public static Repository GetRepository(this ScenarioContext sc) => sc.ScenarioContainer.Resolve<Repository>();
        public static PointerService GetPointerService(this ScenarioContext sc) => sc.ScenarioContainer.Resolve<PointerService>();



        



    }
}
